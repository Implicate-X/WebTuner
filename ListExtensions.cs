using System;
using System.Collections.Generic;

namespace ImplicateX.TinyCLR
{
	public static class ListExtensions
	{
		public static List<T> Where<T>(
			this List<T> source, Predicate<T> predicate )
		{
			var result = new List<T>();

			source.ForEach( item =>
			{
				if( predicate( item ) )
					result.Add( item );
			} );

			return result;
		}

		public static bool CountEquals<T1, T2>(
			this List<T1> source,
			List<T2> other )
		{
			return source.Count == other.Count;
		}
	}
}
