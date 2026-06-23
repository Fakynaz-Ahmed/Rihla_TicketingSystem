using Rihla.Models.Db;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Rihla.Services.Db;

public interface IPassportDataRepository
{
    Task<DbPassportData?> GetByPassportNumberAsync(string passportNumber);
    Task AddAsync(DbPassportData passportData);
    Task UpdateAsync(DbPassportData passportData);
    Task<List<DbPassportData>> GetAllAsync(string? status = null);
}
