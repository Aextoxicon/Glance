using System.Threading.Tasks;
using Glance.Models;
using Glance.Utils;

namespace Glance.Repositories;

public interface IArtifactRepo
{
    Task<Result<IReadOnlyList<IArtifact>>> ListAsync(string path);
    Task<Result<IReadOnlyList<IArtifact>>> SearchAsync(string query);
    Task<Result<IArtifact>> GetAsync(string id);
    Task<Result<bool>> DeleteAsync(string id);
    Task<Result<string>> GetContentUriAsync(string id);
    Task<Result<string>> TryReadTextAsync(string id);
}
