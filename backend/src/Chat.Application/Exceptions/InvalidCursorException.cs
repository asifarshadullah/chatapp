namespace Chat.Application.Exceptions;

/// <summary>Thrown when a conversation list cursor is not in the form the system issues.</summary>
public class InvalidCursorException() : Exception("Invalid cursor.");
