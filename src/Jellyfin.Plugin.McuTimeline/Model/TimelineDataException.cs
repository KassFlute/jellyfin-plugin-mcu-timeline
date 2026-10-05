namespace Jellyfin.Plugin.McuTimeline.Model;

/// <summary>
/// Thrown when a data file does not validate. The message is meant for the administrator.
/// </summary>
public class TimelineDataException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineDataException"/> class.
    /// </summary>
    public TimelineDataException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineDataException"/> class.
    /// </summary>
    /// <param name="message">What is wrong, and where.</param>
    public TimelineDataException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineDataException"/> class.
    /// </summary>
    /// <param name="message">What is wrong, and where.</param>
    /// <param name="innerException">Underlying error.</param>
    public TimelineDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
