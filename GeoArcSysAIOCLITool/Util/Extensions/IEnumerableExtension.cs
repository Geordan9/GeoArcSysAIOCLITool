using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoArcSysAIOCLITool.Util.Extensions;

public static class IEnumerableExtension
{
    public static IEnumerable<TResult> SelectManyNullCheck<TSource, TResult>(
        this IEnumerable<TSource> source,
        Func<TSource, IEnumerable<TResult>> selector)
    {
        return source.Select(selector)
            .Where(e => e != null)
            .SelectMany(e => e);
    }
}