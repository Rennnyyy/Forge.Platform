// Polyfill so C# init-only setters and records compile against netstandard2.0.
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
