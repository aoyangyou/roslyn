﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using Microsoft.CodeAnalysis.ExternalAccess.VSTypeScript.Api;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.ExternalAccess.VSTypeScript;

internal static class VSTypeScriptGlyphHelpers
{
    public static VSTypeScriptGlyph ConvertFrom(Glyph glyph)
    {
        return glyph switch
        {
            Glyph.None => VSTypeScriptGlyph.None,
            Glyph.Assembly => VSTypeScriptGlyph.Assembly,
            Glyph.BasicFile => VSTypeScriptGlyph.BasicFile,
            Glyph.BasicProject => VSTypeScriptGlyph.BasicProject,
            Glyph.ClassPublic => VSTypeScriptGlyph.ClassPublic,
            Glyph.ClassProtected => VSTypeScriptGlyph.ClassProtected,
            Glyph.ClassPrivate => VSTypeScriptGlyph.ClassPrivate,
            Glyph.ClassInternal => VSTypeScriptGlyph.ClassInternal,
            Glyph.CSharpFile => VSTypeScriptGlyph.CSharpFile,
            Glyph.CSharpProject => VSTypeScriptGlyph.CSharpProject,
            Glyph.ConstantPublic => VSTypeScriptGlyph.ConstantPublic,
            Glyph.ConstantProtected => VSTypeScriptGlyph.ConstantProtected,
            Glyph.ConstantPrivate => VSTypeScriptGlyph.ConstantPrivate,
            Glyph.ConstantInternal => VSTypeScriptGlyph.ConstantInternal,
            Glyph.DelegatePublic => VSTypeScriptGlyph.DelegatePublic,
            Glyph.DelegateProtected => VSTypeScriptGlyph.DelegateProtected,
            Glyph.DelegatePrivate => VSTypeScriptGlyph.DelegatePrivate,
            Glyph.DelegateInternal => VSTypeScriptGlyph.DelegateInternal,
            Glyph.EnumPublic => VSTypeScriptGlyph.EnumPublic,
            Glyph.EnumProtected => VSTypeScriptGlyph.EnumProtected,
            Glyph.EnumPrivate => VSTypeScriptGlyph.EnumPrivate,
            Glyph.EnumInternal => VSTypeScriptGlyph.EnumInternal,
            Glyph.EnumMemberPublic => VSTypeScriptGlyph.EnumMemberPublic,
            Glyph.EnumMemberProtected => VSTypeScriptGlyph.EnumMemberProtected,
            Glyph.EnumMemberPrivate => VSTypeScriptGlyph.EnumMemberPrivate,
            Glyph.EnumMemberInternal => VSTypeScriptGlyph.EnumMemberInternal,
            Glyph.Error => VSTypeScriptGlyph.Error,
            Glyph.StatusInformation => VSTypeScriptGlyph.StatusInformation,
            Glyph.EventPublic => VSTypeScriptGlyph.EventPublic,
            Glyph.EventProtected => VSTypeScriptGlyph.EventProtected,
            Glyph.EventPrivate => VSTypeScriptGlyph.EventPrivate,
            Glyph.EventInternal => VSTypeScriptGlyph.EventInternal,
            Glyph.ExtensionMethodPublic => VSTypeScriptGlyph.ExtensionMethodPublic,
            Glyph.ExtensionMethodProtected => VSTypeScriptGlyph.ExtensionMethodProtected,
            Glyph.ExtensionMethodPrivate => VSTypeScriptGlyph.ExtensionMethodPrivate,
            Glyph.ExtensionMethodInternal => VSTypeScriptGlyph.ExtensionMethodInternal,
            Glyph.FieldPublic => VSTypeScriptGlyph.FieldPublic,
            Glyph.FieldProtected => VSTypeScriptGlyph.FieldProtected,
            Glyph.FieldPrivate => VSTypeScriptGlyph.FieldPrivate,
            Glyph.FieldInternal => VSTypeScriptGlyph.FieldInternal,
            Glyph.InterfacePublic => VSTypeScriptGlyph.InterfacePublic,
            Glyph.InterfaceProtected => VSTypeScriptGlyph.InterfaceProtected,
            Glyph.InterfacePrivate => VSTypeScriptGlyph.InterfacePrivate,
            Glyph.InterfaceInternal => VSTypeScriptGlyph.InterfaceInternal,
            Glyph.Intrinsic => VSTypeScriptGlyph.Intrinsic,
            Glyph.Keyword => VSTypeScriptGlyph.Keyword,
            Glyph.Label => VSTypeScriptGlyph.Label,
            Glyph.Local => VSTypeScriptGlyph.Local,
            Glyph.Namespace => VSTypeScriptGlyph.Namespace,
            Glyph.MethodPublic => VSTypeScriptGlyph.MethodPublic,
            Glyph.MethodProtected => VSTypeScriptGlyph.MethodProtected,
            Glyph.MethodPrivate => VSTypeScriptGlyph.MethodPrivate,
            Glyph.MethodInternal => VSTypeScriptGlyph.MethodInternal,
            Glyph.ModulePublic => VSTypeScriptGlyph.ModulePublic,
            Glyph.ModuleProtected => VSTypeScriptGlyph.ModuleProtected,
            Glyph.ModulePrivate => VSTypeScriptGlyph.ModulePrivate,
            Glyph.ModuleInternal => VSTypeScriptGlyph.ModuleInternal,
            Glyph.OpenFolder => VSTypeScriptGlyph.OpenFolder,
            Glyph.Operator => VSTypeScriptGlyph.Operator,
            Glyph.Parameter => VSTypeScriptGlyph.Parameter,
            Glyph.PropertyPublic => VSTypeScriptGlyph.PropertyPublic,
            Glyph.PropertyProtected => VSTypeScriptGlyph.PropertyProtected,
            Glyph.PropertyPrivate => VSTypeScriptGlyph.PropertyPrivate,
            Glyph.PropertyInternal => VSTypeScriptGlyph.PropertyInternal,
            Glyph.RangeVariable => VSTypeScriptGlyph.RangeVariable,
            Glyph.Reference => VSTypeScriptGlyph.Reference,
            Glyph.StructurePublic => VSTypeScriptGlyph.StructurePublic,
            Glyph.StructureProtected => VSTypeScriptGlyph.StructureProtected,
            Glyph.StructurePrivate => VSTypeScriptGlyph.StructurePrivate,
            Glyph.StructureInternal => VSTypeScriptGlyph.StructureInternal,
            Glyph.TypeParameter => VSTypeScriptGlyph.TypeParameter,
            Glyph.Snippet => VSTypeScriptGlyph.Snippet,
            Glyph.CompletionWarning => VSTypeScriptGlyph.CompletionWarning,
            Glyph.AddReference => VSTypeScriptGlyph.AddReference,
            Glyph.NuGet => VSTypeScriptGlyph.NuGet,
            Glyph.TargetTypeMatch => VSTypeScriptGlyph.TargetTypeMatch,
            _ => throw ExceptionUtilities.UnexpectedValue(glyph),
        };
    }

    public static Glyph ConvertTo(VSTypeScriptGlyph glyph)
    {
        return glyph switch
        {
            VSTypeScriptGlyph.None => Glyph.None,
            VSTypeScriptGlyph.Assembly => Glyph.Assembly,
            VSTypeScriptGlyph.BasicFile => Glyph.BasicFile,
            VSTypeScriptGlyph.BasicProject => Glyph.BasicProject,
            VSTypeScriptGlyph.ClassPublic => Glyph.ClassPublic,
            VSTypeScriptGlyph.ClassProtected => Glyph.ClassProtected,
            VSTypeScriptGlyph.ClassPrivate => Glyph.ClassPrivate,
            VSTypeScriptGlyph.ClassInternal => Glyph.ClassInternal,
            VSTypeScriptGlyph.CSharpFile => Glyph.CSharpFile,
            VSTypeScriptGlyph.CSharpProject => Glyph.CSharpProject,
            VSTypeScriptGlyph.ConstantPublic => Glyph.ConstantPublic,
            VSTypeScriptGlyph.ConstantProtected => Glyph.ConstantProtected,
            VSTypeScriptGlyph.ConstantPrivate => Glyph.ConstantPrivate,
            VSTypeScriptGlyph.ConstantInternal => Glyph.ConstantInternal,
            VSTypeScriptGlyph.DelegatePublic => Glyph.DelegatePublic,
            VSTypeScriptGlyph.DelegateProtected => Glyph.DelegateProtected,
            VSTypeScriptGlyph.DelegatePrivate => Glyph.DelegatePrivate,
            VSTypeScriptGlyph.DelegateInternal => Glyph.DelegateInternal,
            VSTypeScriptGlyph.EnumPublic => Glyph.EnumPublic,
            VSTypeScriptGlyph.EnumProtected => Glyph.EnumProtected,
            VSTypeScriptGlyph.EnumPrivate => Glyph.EnumPrivate,
            VSTypeScriptGlyph.EnumInternal => Glyph.EnumInternal,
            VSTypeScriptGlyph.EnumMemberPublic => Glyph.EnumMemberPublic,
            VSTypeScriptGlyph.EnumMemberProtected => Glyph.EnumMemberProtected,
            VSTypeScriptGlyph.EnumMemberPrivate => Glyph.EnumMemberPrivate,
            VSTypeScriptGlyph.EnumMemberInternal => Glyph.EnumMemberInternal,
            VSTypeScriptGlyph.Error => Glyph.Error,
            VSTypeScriptGlyph.StatusInformation => Glyph.StatusInformation,
            VSTypeScriptGlyph.EventPublic => Glyph.EventPublic,
            VSTypeScriptGlyph.EventProtected => Glyph.EventProtected,
            VSTypeScriptGlyph.EventPrivate => Glyph.EventPrivate,
            VSTypeScriptGlyph.EventInternal => Glyph.EventInternal,
            VSTypeScriptGlyph.ExtensionMethodPublic => Glyph.ExtensionMethodPublic,
            VSTypeScriptGlyph.ExtensionMethodProtected => Glyph.ExtensionMethodProtected,
            VSTypeScriptGlyph.ExtensionMethodPrivate => Glyph.ExtensionMethodPrivate,
            VSTypeScriptGlyph.ExtensionMethodInternal => Glyph.ExtensionMethodInternal,
            VSTypeScriptGlyph.FieldPublic => Glyph.FieldPublic,
            VSTypeScriptGlyph.FieldProtected => Glyph.FieldProtected,
            VSTypeScriptGlyph.FieldPrivate => Glyph.FieldPrivate,
            VSTypeScriptGlyph.FieldInternal => Glyph.FieldInternal,
            VSTypeScriptGlyph.InterfacePublic => Glyph.InterfacePublic,
            VSTypeScriptGlyph.InterfaceProtected => Glyph.InterfaceProtected,
            VSTypeScriptGlyph.InterfacePrivate => Glyph.InterfacePrivate,
            VSTypeScriptGlyph.InterfaceInternal => Glyph.InterfaceInternal,
            VSTypeScriptGlyph.Intrinsic => Glyph.Intrinsic,
            VSTypeScriptGlyph.Keyword => Glyph.Keyword,
            VSTypeScriptGlyph.Label => Glyph.Label,
            VSTypeScriptGlyph.Local => Glyph.Local,
            VSTypeScriptGlyph.Namespace => Glyph.Namespace,
            VSTypeScriptGlyph.MethodPublic => Glyph.MethodPublic,
            VSTypeScriptGlyph.MethodProtected => Glyph.MethodProtected,
            VSTypeScriptGlyph.MethodPrivate => Glyph.MethodPrivate,
            VSTypeScriptGlyph.MethodInternal => Glyph.MethodInternal,
            VSTypeScriptGlyph.ModulePublic => Glyph.ModulePublic,
            VSTypeScriptGlyph.ModuleProtected => Glyph.ModuleProtected,
            VSTypeScriptGlyph.ModulePrivate => Glyph.ModulePrivate,
            VSTypeScriptGlyph.ModuleInternal => Glyph.ModuleInternal,
            VSTypeScriptGlyph.OpenFolder => Glyph.OpenFolder,
            VSTypeScriptGlyph.Operator => Glyph.Operator,
            VSTypeScriptGlyph.Parameter => Glyph.Parameter,
            VSTypeScriptGlyph.PropertyPublic => Glyph.PropertyPublic,
            VSTypeScriptGlyph.PropertyProtected => Glyph.PropertyProtected,
            VSTypeScriptGlyph.PropertyPrivate => Glyph.PropertyPrivate,
            VSTypeScriptGlyph.PropertyInternal => Glyph.PropertyInternal,
            VSTypeScriptGlyph.RangeVariable => Glyph.RangeVariable,
            VSTypeScriptGlyph.Reference => Glyph.Reference,
            VSTypeScriptGlyph.StructurePublic => Glyph.StructurePublic,
            VSTypeScriptGlyph.StructureProtected => Glyph.StructureProtected,
            VSTypeScriptGlyph.StructurePrivate => Glyph.StructurePrivate,
            VSTypeScriptGlyph.StructureInternal => Glyph.StructureInternal,
            VSTypeScriptGlyph.TypeParameter => Glyph.TypeParameter,
            VSTypeScriptGlyph.Snippet => Glyph.Snippet,
            VSTypeScriptGlyph.CompletionWarning => Glyph.CompletionWarning,
            VSTypeScriptGlyph.AddReference => Glyph.AddReference,
            VSTypeScriptGlyph.NuGet => Glyph.NuGet,
            VSTypeScriptGlyph.TargetTypeMatch => Glyph.TargetTypeMatch,
            _ => throw ExceptionUtilities.UnexpectedValue(glyph),
        };
    }
}
