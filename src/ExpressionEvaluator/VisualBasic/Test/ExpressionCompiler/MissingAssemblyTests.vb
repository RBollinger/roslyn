﻿' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Collections.Immutable
Imports Microsoft.CodeAnalysis.ExpressionEvaluator
Imports Microsoft.CodeAnalysis.Test.Utilities
Imports Microsoft.CodeAnalysis.VisualBasic.ExpressionEvaluator
Imports Microsoft.CodeAnalysis.VisualBasic.Symbols
Imports Microsoft.VisualStudio.Debugger.Evaluation
Imports Roslyn.Test.PdbUtilities
Imports Roslyn.Test.Utilities
Imports Xunit

Namespace Microsoft.CodeAnalysis.VisualBasic.UnitTests
    Public Class MissingAssemblyTests
        Inherits ExpressionCompilerTestBase

        <Fact>
        Public Sub ErrorsWithAssemblyIdentityArguments()
            Dim identity = New AssemblyIdentity(GetUniqueName())
            Assert.Same(identity, GetMissingAssemblyIdentities(ERRID.ERR_UnreferencedAssembly3, identity).Single())
        End Sub

        <Fact>
        Public Sub ErrorsWithAssemblySymbolArguments()
            Dim identity = New AssemblyIdentity(GetUniqueName())
            Dim assembly = CreateCompilation(identity, {}, {}).Assembly
            Assert.Same(identity, GetMissingAssemblyIdentities(ERRID.ERR_UnreferencedAssemblyEvent3, assembly).Single())
        End Sub

        <Fact>
        Public Sub ErrorsWithAssemblyMultipleSymbolArguments()
            Dim identity1 = New AssemblyIdentity(GetUniqueName())
            Dim assembly1 = CreateCompilation(identity1, {}, {}).Assembly
            Dim identity2 = New AssemblyIdentity(GetUniqueName())
            Dim assembly2 = CreateCompilation(identity2, {}, {}).Assembly
            Assert.Same(identity2, GetMissingAssemblyIdentities(ERRID.ERR_ForwardedTypeUnavailable3, "dummy", assembly1, assembly2).Single())
            Assert.Same(identity1, GetMissingAssemblyIdentities(ERRID.ERR_ForwardedTypeUnavailable3, "dummy", assembly2, assembly1).Single())
            Assert.True(GetMissingAssemblyIdentities(ERRID.ERR_ForwardedTypeUnavailable3, "dummy", assembly1).IsDefault)
            Assert.True(GetMissingAssemblyIdentities(ERRID.ERR_ForwardedTypeUnavailable3, "dummy", "dummy", "dummy", assembly1).IsDefault)
        End Sub

        <Fact>
        Public Sub ErrorsRequiringSystemCore()
            AssertEx.SetEqual(GetMissingAssemblyIdentities(ERRID.ERR_XmlFeaturesNotAvailable),
                              EvaluationContextBase.SystemIdentity,
                              EvaluationContextBase.SystemCoreIdentity,
                              EvaluationContextBase.SystemXmlIdentity,
                              EvaluationContextBase.SystemXmlLinqIdentity)
        End Sub

        <Fact>
        Public Sub ErrorsRequiringVbCore()
            Assert.Equal(EvaluationContextBase.MicrosoftVisualBasicIdentity, GetMissingAssemblyIdentities(ERRID.ERR_MissingRuntimeHelper).Single())
        End Sub

        <Fact>
        Public Sub MultipleAssemblyArguments()
            Dim identity1 = New AssemblyIdentity(GetUniqueName())
            Dim identity2 = New AssemblyIdentity(GetUniqueName())
            Assert.Same(identity1, GetMissingAssemblyIdentities(ERRID.ERR_UnreferencedAssembly3, identity1, identity2).Single())
            Assert.Same(identity2, GetMissingAssemblyIdentities(ERRID.ERR_UnreferencedAssembly3, identity2, identity1).Single())
        End Sub

        <Fact>
        Public Sub NoAssemblyArguments()
            Assert.True(GetMissingAssemblyIdentities(ERRID.ERR_UnreferencedAssembly3).IsDefault)
            Assert.True(GetMissingAssemblyIdentities(ERRID.ERR_UnreferencedAssembly3, "Not an assembly").IsDefault)
        End Sub

        <Fact>
        Public Sub ERR_UnreferencedAssembly3()
            Const libSource = "
Public Class Missing
End Class
"
            Const source = "
Public Class C
    Public Sub M(parameter As Missing)
    End Sub
End Class
"
            Dim libRef = CreateCompilationWithMscorlib({libSource}, {}, TestOptions.DebugDll, assemblyName:="Lib").EmitToImageReference()
            Dim comp = CreateCompilationWithMscorlib({source}, {libRef}, TestOptions.DebugDll)
            Dim context = CreateMethodContextWithReferences(comp, "C.M", MscorlibRef)

            Const expectedError = "(1,1): error BC30652: Reference required to assembly 'Lib, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null' containing the type 'Missing'. Add one to your project."
            Dim expectedMissingAssemblyIdentity = New AssemblyIdentity("Lib")

            Dim resultProperties As ResultProperties = Nothing
            Dim actualError As String = Nothing
            Dim actualMissingAssemblyIdentities As ImmutableArray(Of AssemblyIdentity) = Nothing

            context.CompileExpression(
                DefaultInspectionContext.Instance,
                "parameter",
                DkmEvaluationFlags.TreatAsExpression,
                DiagnosticFormatter.Instance,
                resultProperties,
                actualError,
                actualMissingAssemblyIdentities,
                EnsureEnglishUICulture.PreferredOrNull,
                testData:=Nothing)
            Assert.Equal(expectedError, actualError)
            Assert.Equal(expectedMissingAssemblyIdentity, actualMissingAssemblyIdentities.Single())
        End Sub

        <Fact>
        Public Sub ERR_ForwardedTypeUnavailable3()
            Const il = "
.assembly extern mscorlib { }
.assembly extern pe2 { }
.assembly pe1 { }

.class extern forwarder Forwarded
{
  .assembly extern pe2
}

.class public auto ansi beforefieldinit Dummy
       extends [mscorlib]System.Object
{
  .method public hidebysig specialname rtspecialname 
          instance void  .ctor() cil managed
  {
    ldarg.0
    call       instance void [mscorlib]System.Object::.ctor()
    ret
  }
}
"
            Const vb = "
Class C
    Shared Sub M(d As Dummy)
    End Sub
End Class
"

            Dim ilRef = CompileIL(il, appendDefaultHeader:=False)
            Dim comp = CreateCompilationWithMscorlib({vb}, {ilRef}, TestOptions.DebugDll)
            Dim runtime = CreateRuntimeInstance(comp)
            Dim context = CreateMethodContext(runtime, "C.M")

            Dim expectedMissingAssemblyIdentity As AssemblyIdentity = New AssemblyIdentity("pe2")
            Dim expectedError = $"(1,6): error BC31424: Type 'Forwarded' in assembly '{context.Compilation.Assembly.Identity}' has been forwarded to assembly '{expectedMissingAssemblyIdentity}'. Either a reference to '{expectedMissingAssemblyIdentity}' is missing from your project or the type 'Forwarded' is missing from assembly '{expectedMissingAssemblyIdentity}'."

            Dim resultProperties As ResultProperties = Nothing
            Dim actualError As String = Nothing
            Dim actualMissingAssemblyIdentities As ImmutableArray(Of AssemblyIdentity) = Nothing

            context.CompileExpression(
                DefaultInspectionContext.Instance,
                "New Forwarded()",
                DkmEvaluationFlags.TreatAsExpression,
                DiagnosticFormatter.Instance,
                resultProperties,
                actualError,
                actualMissingAssemblyIdentities,
                EnsureEnglishUICulture.PreferredOrNull,
                testData:=Nothing)
            Assert.Equal(expectedError, actualError)
            Assert.Equal(expectedMissingAssemblyIdentity, actualMissingAssemblyIdentities.Single())
        End Sub

        <Fact>
        Public Sub ERR_XmlFeaturesNotAvailable()
            Const source = "
Public Class C
    Public Sub M()
        dim x = <element/>
    End Sub
End Class
"
            Dim comp = CreateCompilationWithMscorlib({source}, {SystemRef, SystemCoreRef, SystemXmlRef, SystemXmlLinqRef}, TestOptions.DebugDll)
            comp.AssertNoErrors()
            Dim context = CreateMethodContextWithReferences(comp, "C.M", MscorlibRef)

            Const expectedError = "(1,2): error BC31190: XML literals and XML axis properties are not available. Add references to System.Xml, System.Xml.Linq, and System.Core or other assemblies declaring System.Linq.Enumerable, System.Xml.Linq.XElement, System.Xml.Linq.XName, System.Xml.Linq.XAttribute and System.Xml.Linq.XNamespace types."

            Dim resultProperties As ResultProperties = Nothing
            Dim actualError As String = Nothing
            Dim actualMissingAssemblyIdentities As ImmutableArray(Of AssemblyIdentity) = Nothing

            context.CompileExpression(
                DefaultInspectionContext.Instance,
                "<value/>",
                DkmEvaluationFlags.TreatAsExpression,
                DiagnosticFormatter.Instance,
                resultProperties,
                actualError,
                actualMissingAssemblyIdentities,
                EnsureEnglishUICulture.PreferredOrNull,
                testData:=Nothing)
            Assert.Equal(expectedError, actualError)
            AssertEx.SetEqual(actualMissingAssemblyIdentities,
                              EvaluationContextBase.SystemIdentity,
                              EvaluationContextBase.SystemCoreIdentity,
                              EvaluationContextBase.SystemXmlIdentity,
                              EvaluationContextBase.SystemXmlLinqIdentity)
        End Sub

        <Fact>
        Public Sub ERR_MissingRuntimeAssembly()
            Const source = "
Public Class C
    Public Sub M(o As Object)
    End Sub
End Class
"
            Dim comp = CreateCompilationWithMscorlib({source}, options:=TestOptions.DebugDll)
            Dim context = CreateMethodContextWithReferences(comp, "C.M", MscorlibRef)

            Const expectedError = "(1,2): error BC35000: Requested operation is not available because the runtime library function 'Microsoft.VisualBasic.CompilerServices.Operators.CompareObjectEqual' is not defined."
            Dim expectedMissingAssemblyIdentity = EvaluationContextBase.MicrosoftVisualBasicIdentity

            Dim resultProperties As ResultProperties = Nothing
            Dim actualError As String = Nothing
            Dim actualMissingAssemblyIdentities As ImmutableArray(Of AssemblyIdentity) = Nothing

            context.CompileExpression(
                DefaultInspectionContext.Instance,
                "o = o",
                DkmEvaluationFlags.TreatAsExpression,
                DiagnosticFormatter.Instance,
                resultProperties,
                actualError,
                actualMissingAssemblyIdentities,
                EnsureEnglishUICulture.PreferredOrNull,
                testData:=Nothing)
            Assert.Equal(expectedError, actualError)
            Assert.Equal(expectedMissingAssemblyIdentity, actualMissingAssemblyIdentities.Single())
        End Sub

        <WorkItem(1114866)>
        <ConditionalFact(GetType(OSVersionWin8))>
        Public Sub ERR_UndefinedType1()
            Dim source = "
Class C
    Sub M()
    End Sub
End Class
"

            Dim comp = CreateCompilationWithReferences({VisualBasicSyntaxTree.ParseText(source)}, {MscorlibRef}.Concat(WinRtRefs), TestOptions.DebugDll)
            Dim runtimeAssemblies = ExpressionCompilerTestHelpers.GetRuntimeWinMds("Windows.UI")
            Assert.True(runtimeAssemblies.Any())
            Dim context = CreateMethodContextWithReferences(comp, "C.M", ImmutableArray.Create(MscorlibRef).Concat(runtimeAssemblies))
            Dim globalNamespace = context.Compilation.GlobalNamespace

            Dim expectedIdentity = New AssemblyIdentity("Windows.Storage", contentType:=Reflection.AssemblyContentType.WindowsRuntime)

            Dim actualIdentity = EvaluationContext.GetMissingAssemblyIdentitiesHelper(ERRID.ERR_UndefinedType1, {"Windows.Storage"}, globalNamespace).Single()
            Assert.Equal(expectedIdentity, actualIdentity)

            actualIdentity = EvaluationContext.GetMissingAssemblyIdentitiesHelper(ERRID.ERR_UndefinedType1, {"Global.Windows.Storage"}, globalNamespace).Single()
            Assert.Equal(expectedIdentity, actualIdentity)

            actualIdentity = EvaluationContext.GetMissingAssemblyIdentitiesHelper(ERRID.ERR_UndefinedType1, {"Global.Windows.Storage.Additional"}, globalNamespace).Single()
            Assert.Equal(expectedIdentity, actualIdentity)


            expectedIdentity = New AssemblyIdentity("Windows.UI.Xaml", contentType:=Reflection.AssemblyContentType.WindowsRuntime)

            actualIdentity = EvaluationContext.GetMissingAssemblyIdentitiesHelper(ERRID.ERR_UndefinedType1, {"Windows.UI.Xaml"}, globalNamespace).Single()
            Assert.Equal(expectedIdentity, actualIdentity)


            Assert.True(EvaluationContext.GetMissingAssemblyIdentitiesHelper(ERRID.ERR_UndefinedType1, {"Windows.UI.Xaml(Of T)"}, globalNamespace).IsDefault)
        End Sub

        <WorkItem(1124725, "DevDiv")>
        <WorkItem(597, "GitHub")>
        <Fact>
        Public Sub PseudoVariableType()
            Const source = "
Public Class C
    Public Sub M()
    End Sub
End Class
"
            Dim comp = CreateCompilationWithMscorlib({source}, {}, TestOptions.DebugDll)
            Dim context = CreateMethodContextWithReferences(comp, "C.M", CSharpRef, ExpressionCompilerTestHelpers.IntrinsicAssemblyReference)

            Const expectedError = "(1,1): error BC30002: Type 'System.Void' is not defined."
            Dim expectedMissingAssemblyIdentity = comp.Assembly.CorLibrary.Identity

            Dim resultProperties As ResultProperties = Nothing
            Dim actualError As String = Nothing
            Dim actualMissingAssemblyIdentities As ImmutableArray(Of AssemblyIdentity) = Nothing

            context.CompileExpression(
                InspectionContextFactory.Empty.Add("$stowedexception", "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException, Microsoft.CSharp, Version = 4.0.0.0, Culture = neutral, PublicKeyToken = b03f5f7f11d50a3a"),
                "$stowedexception",
                DkmEvaluationFlags.TreatAsExpression,
                DiagnosticFormatter.Instance,
                resultProperties,
                actualError,
                actualMissingAssemblyIdentities,
                EnsureEnglishUICulture.PreferredOrNull,
                testData:=Nothing)

            Assert.Equal(expectedError, actualError)
            Assert.Equal(expectedMissingAssemblyIdentity, actualMissingAssemblyIdentities.Single())
        End Sub

        <WorkItem(1114866)>
        <ConditionalFact(GetType(OSVersionWin8))>
        Public Sub NotYetLoadedWinMds()
            Dim source = "
Class C
    Shared Sub M(f As Windows.Storage.StorageFolder)
    End Sub
End Class
"

            Dim comp = CreateCompilationWithReferences({VisualBasicSyntaxTree.ParseText(source)}, {MscorlibRef}.Concat(WinRtRefs), TestOptions.DebugDll)
            Dim runtimeAssemblies = ExpressionCompilerTestHelpers.GetRuntimeWinMds("Windows.Storage")
            Assert.True(runtimeAssemblies.Any())
            Dim context = CreateMethodContextWithReferences(comp, "C.M", ImmutableArray.Create(MscorlibRef).Concat(runtimeAssemblies))

            Const expectedError = "(1,1): error BC30456: 'UI' is not a member of 'Windows'."
            Dim expectedMissingAssemblyIdentity = New AssemblyIdentity("Windows.UI", contentType:=System.Reflection.AssemblyContentType.WindowsRuntime)

            Dim resultProperties As ResultProperties = Nothing
            Dim actualError As String = Nothing
            Dim actualMissingAssemblyIdentities As ImmutableArray(Of AssemblyIdentity) = Nothing
            context.CompileExpression(
                InspectionContextFactory.Empty,
                "Windows.UI.Colors",
                DkmEvaluationFlags.None,
                DiagnosticFormatter.Instance,
                resultProperties,
                actualError,
                actualMissingAssemblyIdentities,
                EnsureEnglishUICulture.PreferredOrNull,
                testData:=Nothing)
            Assert.Equal(expectedError, actualError)
            Assert.Equal(expectedMissingAssemblyIdentity, actualMissingAssemblyIdentities.Single())
        End Sub

        ''' <remarks>
        ''' Windows.UI.Xaml is the only (win8) winmd with more than two parts.
        ''' </remarks>
        <WorkItem(1114866)>
        <ConditionalFact(GetType(OSVersionWin8))>
        Public Sub NotYetLoadedWinMds_MultipleParts()
            Dim source = "
Class C
    Shared Sub M(c As Windows.UI.Colors)
    End Sub
End Class
"

            Dim comp = CreateCompilationWithReferences({VisualBasicSyntaxTree.ParseText(source)}, {MscorlibRef}.Concat(WinRtRefs), TestOptions.DebugDll)
            Dim runtimeAssemblies = ExpressionCompilerTestHelpers.GetRuntimeWinMds("Windows.UI")
            Assert.True(runtimeAssemblies.Any())
            Dim context = CreateMethodContextWithReferences(comp, "C.M", ImmutableArray.Create(MscorlibRef).Concat(runtimeAssemblies))

            Const expectedError = "(1,1): error BC30456: 'Xaml' is not a member of 'Windows.UI'."
            Dim expectedMissingAssemblyIdentity = New AssemblyIdentity("Windows.UI.Xaml", contentType:=System.Reflection.AssemblyContentType.WindowsRuntime)

            Dim resultProperties As ResultProperties = Nothing
            Dim actualError As String = Nothing
            Dim actualMissingAssemblyIdentities As ImmutableArray(Of AssemblyIdentity) = Nothing
            context.CompileExpression(
                InspectionContextFactory.Empty,
                "Windows.[UI].Xaml.Application",
                DkmEvaluationFlags.None,
                DiagnosticFormatter.Instance,
                resultProperties,
                actualError,
                actualMissingAssemblyIdentities,
                EnsureEnglishUICulture.PreferredOrNull,
                testData:=Nothing)
            Assert.Equal(expectedError, actualError)
            Assert.Equal(expectedMissingAssemblyIdentity, actualMissingAssemblyIdentities.Single())
        End Sub

        <WorkItem(1114866)>
        <ConditionalFact(GetType(OSVersionWin8))>
        Public Sub NotYetLoadedWinMds_GetType()
            Dim source = "
Class C
    Shared Sub M(f As Windows.Storage.StorageFolder)
    End Sub
End Class
"

            Dim comp = CreateCompilationWithReferences({VisualBasicSyntaxTree.ParseText(source)}, {MscorlibRef}.Concat(WinRtRefs), TestOptions.DebugDll)
            Dim runtimeAssemblies = ExpressionCompilerTestHelpers.GetRuntimeWinMds("Windows.Storage")
            Assert.True(runtimeAssemblies.Any())
            Dim context = CreateMethodContextWithReferences(comp, "C.M", ImmutableArray.Create(MscorlibRef).Concat(runtimeAssemblies))

            Const expectedError = "(1,9): error BC30002: Type 'Windows.UI.Colors' is not defined."
            Dim expectedMissingAssemblyIdentity = New AssemblyIdentity("Windows.UI", contentType:=System.Reflection.AssemblyContentType.WindowsRuntime)

            Dim resultProperties As ResultProperties = Nothing
            Dim actualError As String = Nothing
            Dim actualMissingAssemblyIdentities As ImmutableArray(Of AssemblyIdentity) = Nothing
            context.CompileExpression(
                InspectionContextFactory.Empty,
                "GetType([Windows].UI.Colors)",
                DkmEvaluationFlags.None,
                DiagnosticFormatter.Instance,
                resultProperties,
                actualError,
                actualMissingAssemblyIdentities,
                EnsureEnglishUICulture.PreferredOrNull,
                testData:=Nothing)
            Assert.Equal(expectedError, actualError)
            Assert.Equal(expectedMissingAssemblyIdentity, actualMissingAssemblyIdentities.Single())
        End Sub

        Private Function CreateMethodContextWithReferences(comp As Compilation, methodName As String, ParamArray references As MetadataReference()) As EvaluationContext
            Return CreateMethodContextWithReferences(comp, methodName, ImmutableArray.CreateRange(references))
        End Function

        Private Function CreateMethodContextWithReferences(comp As Compilation, methodName As String, references As ImmutableArray(Of MetadataReference)) As EvaluationContext
            Dim exeBytes As Byte() = Nothing
            Dim pdbBytes As Byte() = Nothing
            Dim unusedReferences As ImmutableArray(Of MetadataReference) = Nothing
            Dim result = comp.EmitAndGetReferences(exeBytes, pdbBytes, unusedReferences)
            Assert.True(result)

            Dim runtime = CreateRuntimeInstance(GetUniqueName(), references, exeBytes, New SymReader(pdbBytes))
            Return CreateMethodContext(runtime, methodName)
        End Function

        Private Shared Function GetMissingAssemblyIdentities(code As ERRID, ParamArray arguments As Object()) As ImmutableArray(Of AssemblyIdentity)
            Return EvaluationContext.GetMissingAssemblyIdentitiesHelper(code, arguments, globalNamespace:=Nothing)
        End Function
    End Class
End Namespace