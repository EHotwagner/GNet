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

using Boo.Lang.Compiler.Ast;
using Boo.Lang.Compiler.TypeSystem;
using Boo.Lang.Compiler.TypeSystem.Internal;

namespace Boo.Lang.Compiler.Steps
{
	public abstract class AbstractNamespaceSensitiveVisitorCompilerStep : AbstractVisitorCompilerStep
	{
		override public void Initialize(CompilerContext context)
		{
			base.Initialize(context);
			NameResolutionService.Reset();
		}
		
		protected void EnterNamespace(INamespace ns)
		{
			NameResolutionService.EnterNamespace(ns);
		}
		
		protected INamespace CurrentNamespace
		{
			get { return NameResolutionService.CurrentNamespace; }
		}
		
		protected void LeaveNamespace()
		{
			NameResolutionService.LeaveNamespace();
		}

		override public void OnModule(Module module)
		{
			EnterNamespace(InternalModule.ScopeFor(module));
			VisitTypeDefinitionBody(module);
			Visit(module.AssemblyAttributes);
			LeaveNamespace();
		}

		override public void OnClassDefinition(ClassDefinition node)
		{
			EnterNamespace((INamespace)GetEntity(node));
			VisitTypeDefinitionBody(node);
			LeaveNamespace();
		}

		public override void OnInterfaceDefinition(InterfaceDefinition node)
		{
			EnterNamespace((INamespace)GetEntity(node));
			VisitTypeDefinitionBody(node);
			LeaveNamespace();
		}

		public override void OnStructDefinition(StructDefinition node)
		{
			EnterNamespace((INamespace)GetEntity(node));
			VisitTypeDefinitionBody(node);
			LeaveNamespace();
		}

		public override void OnEnumDefinition(EnumDefinition node)
		{
			EnterNamespace((INamespace)GetEntity(node));
			VisitTypeDefinitionBody(node);
			LeaveNamespace();
		}

		void VisitTypeDefinitionBody(TypeDefinition node)
		{
			Visit(node.Attributes);
			Visit(node.GenericParameters);
			Visit(node.Members);
			LeaveTypeDefinition(node);
		}

		virtual public void LeaveTypeDefinition(TypeDefinition node)
		{
		}
	}
}