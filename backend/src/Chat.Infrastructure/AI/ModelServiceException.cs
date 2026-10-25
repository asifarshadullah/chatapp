namespace Chat.Infrastructure.AI;

/// <summary>A model service refused or failed a request; the message never holds the key.</summary>
public class ModelServiceException : Exception
{
    public ModelServiceException(int status, string serviceError)
        : base($"The model service answered {status}: {serviceError}")
    {
        Status = status;
    }

    public int Status { get; }
}
