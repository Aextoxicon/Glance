using Glance.Native;

namespace Glance.Processing;

public interface ICadParser
{
    ParsedCad ParseCad(byte[] fileBytes, string filename);
    CadGeometry ParseGeometry(byte[] fileBytes, string filename);
}
