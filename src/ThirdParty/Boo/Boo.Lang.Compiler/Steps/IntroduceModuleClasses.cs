﻿#region license
// Copyright (c) 2004, Rodrigo B. de Oliveira (rbo@acm.org)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification,
// are permitted provided that the following conditions are met:
//
//     * Redistributions of source code must retain the above copyright notice,
//     this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above copyright notice,
//     this list of conditions and the following disclaimer in the documentation
//     and/or other materials provided with the distribution.
//     * Neither the name of Rodrigo B. de Oliveira nor the names of its
//     contributors may be used to endorse or promote products derived from this
//     software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
// ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
// WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF
// THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
#endregion

using System.Text;
using Boo.Lang.Compiler.Ast;
using Boo.Lang.Compiler.TypeSystem;
using Boo.Lang.Compiler.TypeSystem.Internal;

namespace Boo.Lang.Compiler.Steps
{
	
	public class IntroduceModuleClasses : AbstractFastVisitorCompilerStep
	{
		public const string ModuleAttributeName = "System.Runtime.CompilerServices.CompilerGlobalScopeAttribute";
		
		public const string EntryPointMethodName = "Main";
		
		public static bool IsModuleClass(TypeMember member)
		{
			return NodeType.ClassDefinition == member.NodeType && member.Attributes.Contains(ModuleAttributeName);
		}
		
		protected IType _booModuleAttributeType;

		public bool ForceModuleClass { get; set; }

		override public void Initialize(CompilerContext context)
		{
			base.Initialize(context);
			_booModuleAttributeType = TypeSystemServices.Map(typeof(System.Runtime.CompilerServices.CompilerGlobalScopeAttribute));
		}
		
		override public void Run()
		{
			Visit(CompileUnit.Modules);

			DetectOutputType();
		}

		private void DetectOutputType()
		{
			if (Parameters.OutputType != CompilerOutputType.Auto)
				return;

			Parameters.OutputType = HasEntryPoint()
				? CompilerOutputType.ConsoleApplication
				: CompilerOutputType.Library;
		}

		private bool HasEntryPoint()
		{
			return null != ContextAnnotations.GetEntryPoint(Context);
		}

		override public void Dispose()
		{
			_booModuleAttributeType = null;
			base.Dispose();
		}
		
		override public void OnModule(Module node)
		{	
			var existingModuleClass = FindModuleClass(node);
			var moduleClass = existingModuleClass ?? NewModuleClassFor(node);

			Method entryPoint = moduleClass.Members["Main"] as Method;
			
			int removed = 0;
			TypeMember[] members = node.Members.ToArray();
			for (int i=0; i<members.Length; ++i)
			{
				TypeMember member = members[i];
				if (member is TypeDefinition) continue;
				if (member.NodeType == NodeType.Method)
				{
					if (EntryPointMethodName == member.Name)
					{
						entryPoint = (Method)member;
					}
					member.Modifiers |= TypeMemberModifiers.Static;
				}
				node.Members.RemoveAt(i-removed);
				moduleClass.Members.Add(member);
				++removed;
			}
			
			if (!node.Globals.IsEmpty)
			{
				Method method = new Method();
				method.IsSynthetic = true;
				method.Parameters.Add(new ParameterDeclaration("argv", new ArrayTypeReference(new SimpleTypeReference("string"))));
				method.ReturnType = CodeBuilder.CreateTypeReference(TypeSystemServices.VoidType);
				method.Body = node.Globals;
				method.LexicalInfo = node.Globals.Statements[0].LexicalInfo;
				method.EndSourceLocation = node.EndSourceLocation;
				method.Name = EntryPointMethodName;
				method.Modifiers = TypeMemberModifiers.Static | TypeMemberModifiers.Private;
				moduleClass.Members.Add(method);
				
				node.Globals = null;
				entryPoint = method;
			}
			
			SetEntryPointIfNecessary(entryPoint);

			if (existingModuleClass != null || ForceModuleClass || (moduleClass.Members.Count > 0))
			{
				if (moduleClass != existingModuleClass)
				{
					moduleClass.Members.Add(AstUtil.CreateConstructor(node, TypeMemberModifiers.Private));
					node.Members.Add(moduleClass);
				}
				InitializeModuleClassEntity(node, moduleClass);
			}
		}

		private ClassDefinition NewModuleClassFor(Module node)
		{
			var moduleClass = new ClassDefinition(node.LexicalInfo)
			              	{
			              		IsSynthetic = true,
			              		Modifiers = TypeMemberModifiers.Public | TypeMemberModifiers.Final | TypeMemberModifiers.Transient,
			              		EndSourceLocation = node.EndSourceLocation,
			              		Name = BuildModuleClassName(node)
			              	};
			moduleClass.Attributes.Add(CreateBooModuleAttribute());
			return moduleClass;
		}

		private void InitializeModuleClassEntity(Module node, ClassDefinition moduleClass)
		{
			InternalModule entity = ((InternalModule)node.Entity);
			if (null != entity) entity.InitializeModuleClass(moduleClass);
		}

		private void SetEntryPointIfNecessary(Method entryPoint)
		{
			if (null == entryPoint)
				return;

			if (Parameters.OutputType == CompilerOutputType.Library)
				return;

			ContextAnnotations.SetEntryPoint(Context, entryPoint);
		}

		ClassDefinition FindModuleClass(Module node)
		{
			ClassDefinition found = null;
			
			foreach (TypeMember member in node.Members)
			{
				if (IsModuleClass(member))
				{
					if (null == found)
					{
						found = (ClassDefinition)member;
					}
					else
					{
						// ERROR: only a single module class is allowed per module
					}
				}
			}
			return found;
		}
		
		Boo.Lang.Compiler.Ast.Attribute CreateBooModuleAttribute()
		{
			Boo.Lang.Compiler.Ast.Attribute attribute = new Boo.Lang.Compiler.Ast.Attribute(ModuleAttributeName);
			attribute.Entity = _booModuleAttributeType;
			return attribute;
		}
		
		string BuildModuleClassName(Module module)
		{
			var moduleName = module.Name;
			if (string.IsNullOrEmpty(moduleName))
			{
				module.Name = Context.GetUniqueName("Module");
				return module.Name;
			}

			var className = new StringBuilder();
			if (!(char.IsLetter(moduleName[0]) || moduleName[0]=='_'))
				className.Append('_');

			className.Append(char.ToUpper(moduleName[0]));
			for (int i = 1; i < moduleName.Length; ++i)
			{
				var c = moduleName[i];
				className.Append(char.IsLetterOrDigit(c) ? c : '_');
			}
			return className.Append("Module").ToString();
		}
	}
}

