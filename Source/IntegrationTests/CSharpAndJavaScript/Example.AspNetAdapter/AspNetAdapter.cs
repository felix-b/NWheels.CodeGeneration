﻿using System;
using System.Collections.Immutable;
using System.IO;

namespace Example.AspNetAdapter
{
    public class AspNetAdapter
    {
        public AspNetAdapter(Func<string, Stream> outputStreamFactory)
        {
        }

        public ImmutableDictionary<string, Stream> GenerateImplementations(WebUIModel.Metadata.WebUIMetadata metadata) =>
            ImmutableDictionary<string, Stream>.Empty;
    }
}