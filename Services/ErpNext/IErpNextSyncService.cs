using Rihla.DTOs;

namespace Rihla.Services.ErpNext;

public interface IErpNextSyncService
{
    /// <summary>
    /// Synchronize users from ERPNext into the local database.
    /// </summary>
    Task<SyncResultDto> SyncUsersAsync();
}
