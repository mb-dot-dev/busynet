namespace Busynet.Core;

/// <summary>
/// Base type for every expected, user-facing failure. The CLI prints <see cref="Exception.Message"/>
/// as a single stderr line and exits 1. Messages must never contain a bar or Claude token.
/// </summary>
public abstract class BusynetException(string message) : Exception(message);
