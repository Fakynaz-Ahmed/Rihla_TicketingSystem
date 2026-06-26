using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Rihla.DTOs;
using Rihla.Services.Db;
using Rihla.Services.Products;
using System.Security.Claims;

namespace Rihla.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly IAppUserRepository _userRepo;
    private readonly ILogger<ProductsController> _logger;

    public ProductsController(
        IProductService productService,
        IAppUserRepository userRepo,
        ILogger<ProductsController> _logger)
    {
        _productService = productService;
        _userRepo = userRepo;
        this._logger = _logger;
    }

    /// <summary>Get authenticated customer's purchased items.</summary>
    [HttpGet("customer")]
    public async Task<IActionResult> GetCustomerProducts()
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.ErpNextUserId))
            {
                return NotFound(new ApiErrorResponse { Message = "المستخدم أو حساب ERPNext غير موجود." });
            }

            var items = await _productService.GetCustomerProductsAsync(user.ErpNextUserId);
            return Ok(ApiResponse<List<UserProductDto>>.Ok(items));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer products");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب منتجات العميل." });
        }
    }

    /// <summary>Sync authenticated customer's purchased items from ERPNext.</summary>
    [HttpPost("customer/sync")]
    public async Task<IActionResult> SyncCustomerProducts()
    {
        try
        {
            var userId = GetUserId();
            var user = await _userRepo.GetByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.ErpNextUserId))
            {
                return NotFound(new ApiErrorResponse { Message = "المستخدم أو حساب ERPNext غير موجود." });
            }

            var result = await _productService.SyncCustomerProductsAsync(user.ErpNextUserId);
            return Ok(ApiResponse<SyncResultDto>.Ok(result, "تمت مزامنة مشتريات العميل بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing customer products");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل مزامنة منتجات العميل." });
        }
    }

    /// <summary>Get catalog products list (supports search, category filter, and paging).</summary>
    [HttpGet]
    public async Task<IActionResult> GetProducts(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var result = await _productService.GetAllProductsPagedAsync(search, category, page, pageSize);
            return Ok(ApiResponse<ProductsResultDto>.Ok(result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting products catalog");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب كتالوج المنتجات." });
        }
    }

    /// <summary>Get product details by ID.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetProductById(int id)
    {
        try
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null)
            {
                return NotFound(new ApiErrorResponse { Message = "المنتج غير موجود." });
            }
            return Ok(ApiResponse<ProductDto>.Ok(product));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting product by id {Id}", id);
            return StatusCode(500, new ApiErrorResponse { Message = "فشل جلب تفاصيل المنتج." });
        }
    }

    /// <summary>Sync catalog products list from ERPNext.</summary>
    [HttpPost("sync")]
    [Authorize(Roles = "System Manager")]
    public async Task<IActionResult> SyncProducts()
    {
        try
        {
            var result = await _productService.SyncProductsAsync();
            return Ok(ApiResponse<SyncResultDto>.Ok(result, "تمت مزامنة كتالوج المنتجات بنجاح."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing products");
            return StatusCode(500, new ApiErrorResponse { Message = "فشل مزامنة كتالوج المنتجات." });
        }
    }

    private int GetUserId()
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        return int.TryParse(userIdStr, out var id) ? id : 0;
    }
}
