using Rihla.DTOs;

namespace Rihla.Services.Products;

public interface IProductService
{
    Task<List<UserProductDto>> GetCustomerProductsAsync(string customerErpNextUserId);
    Task<SyncResultDto> SyncProductsAsync();
    Task<SyncResultDto> SyncCustomerProductsAsync(string customerErpNextUserId);
    Task<List<ProductDto>> GetAllProductsAsync();
    Task<ProductsResultDto> GetAllProductsPagedAsync(string? search, string? category, int page, int pageSize);
    Task<ProductDto?> GetProductByIdAsync(int id);
    Task<ProductDto?> GetProductByErpNextIdAsync(string erpNextId);
}
