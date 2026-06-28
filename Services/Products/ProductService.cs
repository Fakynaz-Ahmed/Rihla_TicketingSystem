using Microsoft.Extensions.Logging;
using Rihla.DTOs;
using Rihla.Models.Db;
using Rihla.Services.Db;
using Rihla.Services.ErpNext;

namespace Rihla.Services.Products;

public class ProductService : IProductService
{
    private readonly IErpNextClient _erpClient;
    private readonly IProductRepository _productRepo;
    private readonly IUserProductRepository _userProductRepo;
    private readonly ILogger<ProductService> _logger;

    public ProductService(
        IErpNextClient erpClient,
        IProductRepository productRepo,
        IUserProductRepository userProductRepo,
        ILogger<ProductService> logger)
    {
        _erpClient = erpClient;
        _productRepo = productRepo;
        _userProductRepo = userProductRepo;
        _logger = logger;
    }

    public async Task<List<UserProductDto>> GetCustomerProductsAsync(string customerErpNextUserId)
    {
        var dbItems = await _userProductRepo.GetByCustomerAsync(customerErpNextUserId);
        return dbItems.Select(MapUserProduct).ToList();
    }

    public async Task<SyncResultDto> SyncProductsAsync()
    {
        try
        {
            _logger.LogInformation("Starting ERPNext items sync...");
            var itemsRaw = await _erpClient.GetDocListAsync(
                "Item",
                filters: new Dictionary<string, string> { ["disabled"] = "0" },
                fields: new List<string> { "name", "item_name", "item_code", "description", "standard_rate", "item_group", "image" },
                limit: 1000
            );

            if (itemsRaw == null || itemsRaw.Count == 0)
            {
                return new SyncResultDto { Success = true, Message = "No active items found in ERPNext." };
            }

            var products = itemsRaw.Select(r =>
            {
                var name = r.GetValueOrDefault("item_name")?.ToString() ?? r.GetValueOrDefault("name")?.ToString() ?? string.Empty;
                var itemCode = r.GetValueOrDefault("item_code")?.ToString() ?? r.GetValueOrDefault("name")?.ToString() ?? string.Empty;
                var desc = r.GetValueOrDefault("description")?.ToString() ?? string.Empty;
                var rateVal = r.GetValueOrDefault("standard_rate");
                var rate = rateVal is double d ? (decimal)d : (rateVal is float f ? (decimal)f : 0m);
                var category = r.GetValueOrDefault("item_group")?.ToString() ?? string.Empty;
                var image = r.GetValueOrDefault("image")?.ToString();

                return new DbProduct
                {
                    ErpNextId = itemCode,
                    Name = name,
                    DisplayName = name,
                    ItemCode = itemCode,
                    Description = desc,
                    ListPrice = rate,
                    Category = category,
                    Active = true,
                    ImageUrl = image
                };
            }).ToList();

            await _productRepo.UpsertManyAsync(products);
            return new SyncResultDto { Success = true, Message = $"Successfully synced {products.Count} products from ERPNext." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncProductsAsync failed");
            return new SyncResultDto { Success = false, Message = $"Sync failed: {ex.Message}" };
        }
    }

    public async Task<SyncResultDto> SyncCustomerProductsAsync(string customerErpNextUserId)
    {
        try
        {
            _logger.LogInformation("Starting ERPNext customer purchased products sync for {Customer}...", customerErpNextUserId);
            var filters = new Dictionary<string, string> { ["customer"] = customerErpNextUserId };
            var orders = await _erpClient.GetDocListAsync(
                "Sales Order",
                filters,
                new List<string> { "name", "transaction_date", "currency" },
                limit: 100
            );

            var list = new List<DbUserProduct>();
            foreach (var order in orders)
            {
                var orderName = order.GetValueOrDefault("name")?.ToString();
                if (string.IsNullOrEmpty(orderName)) continue;

                var fullOrder = await _erpClient.GetDocAsync("Sales Order", orderName);
                if (fullOrder is null || !fullOrder.TryGetValue("items", out var itemsObj) || itemsObj is not List<object?> itemsList)
                    continue;

                var orderDateStr = order.GetValueOrDefault("transaction_date")?.ToString();
                var orderDate = DateTime.TryParse(orderDateStr, out var d) ? d : DateTime.UtcNow;
                var currency = order.GetValueOrDefault("currency")?.ToString() ?? "USD";

                foreach (var itemObj in itemsList)
                {
                    if (itemObj is not Dictionary<string, object?> itemDict) continue;

                    var lineName = itemDict.GetValueOrDefault("name")?.ToString();
                    var itemCode = itemDict.GetValueOrDefault("item_code")?.ToString();
                    var itemName = itemDict.GetValueOrDefault("item_name")?.ToString() ?? itemCode;
                    var rateVal = itemDict.GetValueOrDefault("rate");
                    var rate = rateVal is double r ? (decimal)r : (rateVal is float f ? (decimal)f : 0m);

                    if (string.IsNullOrEmpty(lineName) || string.IsNullOrEmpty(itemCode)) continue;

                    var localProduct = await _productRepo.GetByErpNextIdAsync(itemCode);

                    list.Add(new DbUserProduct
                    {
                        ErpNextLineName = lineName,
                        CustomerErpNextUserId = customerErpNextUserId,
                        ProductId = localProduct?.Id,
                        ItemCode = itemCode,
                        Name = itemName,
                        OrderReference = orderName,
                        OrderDate = orderDate,
                        Price = rate,
                        Currency = currency,
                        SyncedAt = DateTime.UtcNow
                    });
                }
            }

            await _userProductRepo.UpsertManyAsync(list);
            return new SyncResultDto { Success = true, Message = $"Successfully synced {list.Count} purchased items." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncCustomerProductsAsync failed for {Customer}", customerErpNextUserId);
            return new SyncResultDto { Success = false, Message = $"Sync failed: {ex.Message}" };
        }
    }

    public async Task<List<ProductDto>> GetAllProductsAsync()
    {
        var list = await _productRepo.GetAllAsync();
        return list.Select(MapProduct).ToList();
    }

    public async Task<ProductsResultDto> GetAllProductsPagedAsync(string? search, string? category, int page, int pageSize)
    {
        var (items, total) = await _productRepo.GetFilteredAsync(search, category, page, pageSize);
        return new ProductsResultDto
        {
            Items = items.Select(MapProduct).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ProductDto?> GetProductByIdAsync(int id)
    {
        var p = await _productRepo.GetByIdAsync(id);
        return p is null ? null : MapProduct(p);
    }

    public async Task<ProductDto?> GetProductByErpNextIdAsync(string erpNextId)
    {
        var p = await _productRepo.GetByErpNextIdAsync(erpNextId);
        return p is null ? null : MapProduct(p);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProductDto MapProduct(DbProduct p) => new()
    {
        Id = p.Id,
        ErpNextId = p.ErpNextId,
        Name = p.Name,
        DisplayName = p.DisplayName,
        ItemCode = p.ItemCode,
        Description = p.Description,
        ListPrice = p.ListPrice,
        Category = p.Category,
        Active = p.Active,
        ImageUrl = p.ImageUrl
    };

    private static UserProductDto MapUserProduct(DbUserProduct up) => new()
    {
        Id = up.Id,
        ErpNextLineName = up.ErpNextLineName,
        CustomerErpNextUserId = up.CustomerErpNextUserId,
        ProductId = up.ProductId,
        ItemCode = up.ItemCode,
        Name = up.Name,
        OrderReference = up.OrderReference,
        OrderDate = up.OrderDate,
        Price = up.Price,
        Currency = up.Currency,
        Product = up.Product is null ? null : MapProduct(up.Product)
    };
}
