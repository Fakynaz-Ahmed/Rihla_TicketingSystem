using Rihla.Models.Db;

namespace Rihla.Services.Db;

public interface IUserProductRepository
{
    Task UpsertManyAsync(IEnumerable<DbUserProduct> userProducts);
    Task<List<DbUserProduct>> GetByCustomerAsync(string customerErpNextUserId);
    Task<DbUserProduct?> GetByLineNameAndCustomerAsync(string lineName, string customerErpNextUserId);
    Task<DbUserProduct?> GetByProductIdAndCustomerAsync(int productId, string customerErpNextUserId);
    Task<bool> ProductOwnedByCustomerAsync(int productId, string customerErpNextUserId);
}
