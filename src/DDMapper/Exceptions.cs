namespace AutoMapper;

/// <summary>Base exception for every failure raised by DDMapper.</summary>
public class AutoMapperException : Exception
{
    public AutoMapperException(string message) : base(message) { }
    public AutoMapperException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Raised when a mapping configuration is invalid or used after it was sealed.</summary>
public class AutoMapperConfigurationException : AutoMapperException
{
    public AutoMapperConfigurationException(string message) : base(message) { }
    public AutoMapperConfigurationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Raised when a mapping cannot be performed at runtime.</summary>
public class AutoMapperMappingException : AutoMapperException
{
    public AutoMapperMappingException(string message) : base(message) { }
    public AutoMapperMappingException(string message, Exception inner) : base(message, inner) { }
}
