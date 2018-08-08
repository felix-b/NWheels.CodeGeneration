﻿using System;
using System.Collections.Generic;
using System.Text;
using MetaPrograms.CodeModel.Imperative;

namespace MetaPrograms.Adapters.Roslyn
{
    public static class LanguageInfoExtensions
    {
        private static readonly LanguageInfo CSharpLanguage = new LanguageInfo(name: "C#", abbreviation: "CS");

        public static LanguageInfo CSharp(this LanguageInfo.ExtensibleEntries entries) => CSharpLanguage;
    }
}
