﻿using System;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.Application.Progress;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.ContextActions;
using JetBrains.ReSharper.Feature.Services.CSharp.Analyses.Bulbs;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.ReSharper.Psi.Search;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.TextControl;
using JetBrains.Util;

namespace AsyncConverter
{
    [ContextAction(Group = "C#", Name = "ConvertToAsync", Description = "Convert method to async and replace all inner call to async version if exist.")]
    public class MathodToAsyncConverter : ContextActionBase
    {
        private ICSharpContextActionDataProvider Provider { get; }

        public MathodToAsyncConverter(ICSharpContextActionDataProvider provider)
        {
            Provider = provider;
        }

        protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
        {
            var method = GetMethodFromCarretPosition();

            var methodDeclaredElement = method?.DeclaredElement;
            if (methodDeclaredElement == null)
                return null;

            var finder = Provider.PsiServices.Finder;

            var psiModule = method.GetPsiModule();
            var factory = CSharpElementFactory.GetInstance(psiModule);

            FindAndReplaceBaseMethods(finder, psiModule, factory, methodDeclaredElement);

            foreach (var immediateBaseMethod in AsyncHelper.FindImplementingMembers(methodDeclaredElement, NullProgressIndicator.Instance))
            {
                var implementingMethod = immediateBaseMethod.OverridableMember as IMethod;
                var implementingMethodDeclaration = implementingMethod?.GetSingleDeclaration<IMethodDeclaration>();
                if (implementingMethodDeclaration == null)
                    return null;
                ReplaceMethodToAsync(finder, psiModule, factory, implementingMethodDeclaration);
            }

            ReplaceMethodToAsync(finder, psiModule, factory, method);

            return null;
        }

        private void FindAndReplaceBaseMethods(IFinder finder, IPsiModule psiModule, CSharpElementFactory factory, IDeclaredElement methodDeclaredElement)
        {
            foreach (var immediateBaseMethod in finder.FindImmediateBaseElements(methodDeclaredElement, NullProgressIndicator.Instance))
            {
                var baseMethodDeclarations = immediateBaseMethod.GetDeclarations();
                foreach (var declaration in baseMethodDeclarations.OfType<IMethodDeclaration>())
                {
                    FindAndReplaceBaseMethods(finder, psiModule, factory, immediateBaseMethod);
                    ReplaceMethodToAsync(finder, psiModule, factory, declaration);
                }
            }
        }

        private void ReplaceMethodToAsync(IFinder finder, IPsiModule psiModule, CSharpElementFactory factory, IMethodDeclaration method)
        {
            var methodDeclaredElement = method.DeclaredElement;
            if (methodDeclaredElement == null)
                return;

            var usages = finder.FindReferences(methodDeclaredElement, SearchDomainFactory.Instance.CreateSearchDomain(psiModule), NullProgressIndicator.Instance);
            foreach (var usage in usages)
            {
                var invocation = usage.GetTreeNode().Parent as IInvocationExpression;
                var containingFunctionDeclarationIgnoringClosures = invocation?.InvokedExpression.GetContainingFunctionDeclarationIgnoringClosures();
                if (containingFunctionDeclarationIgnoringClosures == null)
                    continue;
                AsyncHelper.ReplaceCallToAsync(invocation, factory, containingFunctionDeclarationIgnoringClosures.IsAsync);
            }
            var invocationExpressions = method.Body.Descendants<IInvocationExpression>();
            foreach (var invocationExpression in invocationExpressions)
            {
                AsyncHelper.TryReplaceInvocationToAsync(invocationExpression, factory);
            }

            AsyncHelper.ReplaceMethodSignatureToAsync(methodDeclaredElement, psiModule, method);
        }

        public override string Text { get; } = "Convert method to async and replace all inner call to async version if exist.";
        public override bool IsAvailable(IUserDataHolder cache)
        {
            var method = GetMethodFromCarretPosition();
            if (method == null)
                return false;

            var returnType = method.DeclaredElement?.ReturnType;

            return returnType != null && !(returnType.IsTask() || returnType.IsGenericTask());
        }

        [CanBeNull]
        private IMethodDeclaration GetMethodFromCarretPosition()
        {
            var identifier = Provider.TokenAfterCaret as ICSharpIdentifier;
            identifier = identifier ?? Provider.TokenBeforeCaret as ICSharpIdentifier;
            return identifier?.Parent as IMethodDeclaration;
        }
    }
}