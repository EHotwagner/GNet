﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Timers;

using Timer = System.Timers.Timer;
using GNet.IO;
using GNet.PInvoke;

namespace GNet.Profiler.MacroSystem
{
    public class MacroRunner
    {
        // for using PriorityQueue as a max-priority queue
        // (i.e. priority queue which extracts elements with maximum priorities first)
        class MacroPriorityComparer : IComparer<int>
        {
            public int Compare(int x, int y)
            {
                // "inverted" comparison
                // direct comparison of integers should return x - y
                return y - x;
            }
        }

        public const string runEventName = @"Local\RunEvent";
        public const string runExitName = @"Local\RunExit";

        EventWaitHandle runEvent;
        EventWaitHandle runExit;

        bool running;
        //bool releasing;

        ThreadStart runDelegate;
        Thread runThread;

        //Queue<Macro> macroQueue;

        PriorityQueue<int, Macro> macroQueue;
        Stack<Macro> macroStack;

        List<InputWrapper[]> releaseList;
        Dictionary<InputWrapper, int> releaseLookup;

        Macro currentMacro;
        Macro currentMacroTop;
        //Macro canceledMacro;
        //object cancelLock = new object();

        Timer timer;
        //bool timerAborted;

        Stopwatch stopwatch;

        Dictionary<string, Macro> macroLookup;

        public MacroRunner(Profile profile)
        {
            ResetProfile(profile);

            //macroQueue = new Queue<Macro>();
            macroQueue = new PriorityQueue<int, Macro>(new MacroPriorityComparer());

            runDelegate = new ThreadStart(Run);

            timer = new Timer();
            timer.Elapsed += new ElapsedEventHandler(timer_Elapsed);
            timer.AutoReset = false;

            stopwatch = new Stopwatch();
            stopwatch.Start();
        }

        public void ResetProfile(Profile profile)
        {
            //macroLookup = profile.Macros.ToDictionary(x => x.Name);
            foreach (var macro in profile.Macros)
                AddMacro(macro, null);
        }

        void AddMacro(Macro macro, string path)
        {
            string name = path == null ? macro.Name : path + "." + macro.Name;
            macroLookup.Add(name, macro);

            foreach (var step in macro.Steps)
                if (step.Type == StepType.Macro)
                    AddMacro(step as Macro, name);
        }

        public void Start()
        {
            if (running)
                return;

            macroStack = new Stack<Macro>();

            releaseList = new List<InputWrapper[]>();
            releaseLookup = new Dictionary<InputWrapper, int>();

            running = true;
            //releasing = false;

            runEvent = new EventWaitHandle(false, EventResetMode.AutoReset, runEventName);
            runExit = new EventWaitHandle(false, EventResetMode.AutoReset, runExitName);

            runThread = new Thread(runDelegate);
            runThread.Start();
        }

        public void Stop()
        {
            if (!running)
                return;

            running = false;

            runEvent.Set();
            runEvent.Close();

            if (Thread.CurrentThread != runThread)
            {
                if (!runExit.WaitOne(1000))
                {
                    System.Diagnostics.Debug.WriteLine("KeyRepeater.Stop: keyRepeatStop timed out");
                    runThread.Abort();
                }
            }

            runExit.Close();
            runThread = null;
        }

        void Run()
        {
            Step step;
            StepAction action;
            StepActionInput actionInput;
            Delay delay;
            bool stepEnabled;
            long elapsedMs;
            InputWrapper[] release;
            int releaseIndex;
            //Macro macro;
            Macro tempMacro;
            //CancelMacro cancel;
            int loopCount;
            int currentLoop;
            Random rand = new Random();
            Enabler enablerStep;

            var threadRunExit = new EventWaitHandle(false, EventResetMode.AutoReset, runExitName);

            while (running)
            {
                if (currentMacro == null)
                {
                    // pop the parent macro off the stack, or get the next macro from the queue
                    if (macroStack.Count > 0)
                        currentMacro = macroStack.Pop();
                    else if (macroQueue.Count > 0)
                    {
                        lock (macroQueue)
                        {
                            currentMacro = currentMacroTop = macroQueue.Dequeue().Value;
                        }

                        currentMacro.Reset();
                    }
                }

                #region old cancel code
                //if (currentMacro != null)
                //{
                //    if (macroQueue.Count > 0)
                //    {
                //        lock (macroQueue)
                //        {
                //            macro = macroQueue.Peek().Value;
                //        }

                //        cancel = macro as CancelMacro;
                //        if (cancel != null)
                //        {
                //            if (cancel.Macro == currentMacroTop)
                //            {
                //                timer.Stop();
                //                Release();

                //                currentMacro = currentMacroTop = null;

                //                lock (macroQueue)
                //                {
                //                    macroQueue.Dequeue();
                //                }
                //            }
                //        }
                //        else if (macro.IsCanceling && macro.Priority >= currentMacro.Priority)
                //        {
                //            timer.Stop();
                //            Release();

                //            lock (macroQueue)
                //            {
                //                currentMacro = currentMacroTop = macroQueue.Dequeue().Value;
                //            }

                //            currentMacro.Reset();
                //        }
                //    }
                //}
                #endregion

                if (currentMacro == null)
                {
                    runEvent.WaitOne();
                }
                else
                {
                    #region Check for cancelation

                    if (currentMacroTop != null)
                    {
                        // check if the current top-level macro should be canceled
                        if (currentMacroTop.Canceled)
                        {
                            macroStack.Clear();
                            currentMacro = currentMacroTop = null;
                            Release();

                            // start the loop over to get the next macro off the queue (if any)
                            continue;
                        }
                        // check if a child macro should be canceled
                        // note that if the current macro is the top macro, that case has
                        // already been handeld
                        else if (currentMacro.Canceled)
                        {
                            currentMacro = null;

                            // start the loop over to get the parent macro off the stack
                            continue;
                        }

                        // check to see if the next macro in the queue should cancel the current one
                        lock (macroQueue)
                        {
                            if (macroQueue.Count > 0)
                                tempMacro = macroQueue.PeekValue();
                            else
                                tempMacro = null;
                        }

                        if (ShouldCancelCurrent(tempMacro))
                        {
                            macroStack.Clear();
                            currentMacro = currentMacroTop = null;
                            Release();

                            // start the loop over to get the next macro off the queue (if any)
                            continue;
                        }
                    }

                    #endregion

                    step = currentMacro.CurrentStep;
                    currentMacro.IncStep();

                    if (step == null)
                    {
                        Release();

                        loopCount = currentMacro.LoopCount;
                        currentLoop = currentMacro.CurrentLoop;
                        currentMacro.IncLoop();

                        if (loopCount < 0 || currentLoop < loopCount - 1)
                        {
                            System.Diagnostics.Debug.WriteLine("loopCount: " + loopCount + ", currentLoop = " + currentLoop);
                            currentMacro.ResetSteps();
                        }
                        else
                            currentMacro = null;
                    }
                    else
                    {
                        elapsedMs = stopwatch.ElapsedMilliseconds;

                        stepEnabled = step.IsEnabled;
                        if (stepEnabled)
                        {
                            if (step.Timestamp > 0 && step.Timestamp + step.Cooldown > elapsedMs)
                                stepEnabled = false;
                        }

                        if (stepEnabled)
                        {
                            step.Timestamp = elapsedMs;

                            switch (step.Type)
                            {
                                case StepType.Delay:
                                    delay = step as Delay;
                                    
                                    if (delay.RandomRange != null)
                                    {
                                        timer.Interval =
                                            delay.Milliseconds +
                                            (rand.NextDouble() - .5d) * delay.RandomRange.Value;
                                    }
                                    else
                                        timer.Interval = delay.Milliseconds;

                                    timer.Start();
                                    runEvent.WaitOne();
                                    break;

                                case StepType.Release:
                                    Release();
                                    break;

                                case StepType.Macro:
                                    macroStack.Push(currentMacro);
                                    currentMacro = step as Macro;
                                    currentMacro.Reset();
                                    break;

                                case StepType.Action:
                                    action = step as StepActionInput;
                                    action.Run();
                                    break;

                                case StepType.ActionInput:
                                    actionInput = step as StepActionInput;
                                    release = actionInput.Release;
                                    actionInput.Run();

                                    if (release != null)
                                    {
                                        if (releaseLookup.TryGetValue(release[0], out releaseIndex))
                                            releaseList[releaseIndex] = null;
                                        releaseLookup[release[0]] = releaseList.Count;
                                        releaseList.Add(release);
                                    }

                                    if (actionInput.Inputs != null)
                                    {
                                        foreach (var input in actionInput.Inputs)
                                            if (releaseLookup.TryGetValue(input, out releaseIndex))
                                                releaseList[releaseIndex] = null;
                                    }
                                    break;

                                case StepType.Enable:
                                    enablerStep = step as Enable;
                                    if (macroLookup.TryGetValue(enablerStep.Path, out tempMacro))
                                        tempMacro.IsEnabled = true;
                                    break;

                                case StepType.Disable:
                                    enablerStep = step as Disable;
                                    if (macroLookup.TryGetValue(enablerStep.Path, out tempMacro))
                                        tempMacro.IsEnabled = false;
                                    break;
                            }
                        }
                    }
                }
            }

            threadRunExit.Set();
            threadRunExit.Close();
        }

        public void Release()
        {
            //releasing = true;

            InputWrapper[] release;
            for (int i = releaseList.Count - 1; i >= 0; i--)
            {
                release = releaseList[i];
                if (release != null)
                    Interop.SendInput((uint)release.Length, release);
            }

            releaseList.Clear();
            releaseLookup.Clear();
        }

        bool ShouldCancelCurrent(Macro cancelingMacro)
        {
            //var a = currentMacro != null;
            //var b = cancelingMacro != null;

            //System.Diagnostics.Debug.Write(" a: " + a ?? "null");
            //System.Diagnostics.Debug.Write(" b: " + b ?? "null");

            //if (a && b)
            //{
            //    var c = cancelingMacro.IsCanceling != CancelingType.None;
            //    var d = cancelingMacro.CancelLevel >= currentMacro.CancelLevel;
            //    var e = currentMacro.IsCancelable;
            //    var f = cancelingMacro.IsCanceling == CancelingType.Forced;
            //    System.Diagnostics.Debug.Write(" c: " + c);
            //    System.Diagnostics.Debug.Write(" d: " + d);
            //    System.Diagnostics.Debug.Write(" e: " + e);
            //    System.Diagnostics.Debug.Write(" f: " + f);
            //}

            //System.Diagnostics.Debug.WriteLine("");

            return
                currentMacro != null &&
                cancelingMacro != null &&
                cancelingMacro.IsCanceling != CancelingType.None &&
                cancelingMacro.CancelLevel >= currentMacro.CancelLevel &&
                (
                    currentMacro.IsCancelable ||
                    cancelingMacro.IsCanceling == CancelingType.Forced
                );
        }

        public void Enqueue(Macro macro)
        {
            lock (macroQueue)
            {
                //var cancel = macro as CancelMacro;
                //if (cancel != null)
                //{
                //    if (cancel.Macro == currentMacroTop)
                //    {
                //        timer.Stop();
                //        macroQueue.Enqueue(macro.Priority, macro);
                //    }
                //}
                //else
                //{
                //    macroQueue.Enqueue(macro.Priority, macro);

                //    if (currentMacro != null && macro.IsCanceling && macro.Priority >= currentMacro.Priority)
                //        timer.Stop();
                //}

                macroQueue.Enqueue(macro.Priority, macro);

                if (macroQueue.PeekValue() == macro && ShouldCancelCurrent(macro))
                    timer.Stop();
            }

            if(!timer.Enabled)
                runEvent.Set();
        }

        //public void Cancel(Macro macro)
        //{
        //    if (macro == null)
        //        return;

        //    lock (cancelLock)
        //    {
        //        if (macro == currentMacro)
        //        {
        //            canceledMacro = macro;
        //            if (timer.Enabled)
        //            {
        //                timer.Stop();
        //                runEvent.Set();
        //            }
        //        }
        //    }
        //}

        void timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            runEvent.Set();
        }
    }
}
