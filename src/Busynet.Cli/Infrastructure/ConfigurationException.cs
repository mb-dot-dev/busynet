using Busynet.Core;

namespace Busynet.Cli.Infrastructure;

/// <summary>
/// A setting from the environment is unusable. The message names the setting and never echoes its value,
/// and no inner exception is kept: those messages can quote the value, which may be a token.
/// </summary>
public sealed class ConfigurationException(string message) : BusynetException(message);
