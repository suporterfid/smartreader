namespace SmartReaderStandalone.Authentication;

public class AuthSchemeConstants
{
    public const string SmartreaderAuthScheme = "SmartreaderAuthScheme";
    public const string NToken = $"{SmartreaderAuthScheme} (?<token>.*)";
}