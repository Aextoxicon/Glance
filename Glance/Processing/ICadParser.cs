namespace Glance.Processing;

public interface ICadParser
{
    ParsedCad ParseCad(byte[] fileBytes, string filename);
}
