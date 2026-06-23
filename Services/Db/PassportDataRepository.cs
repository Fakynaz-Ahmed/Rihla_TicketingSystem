using Microsoft.EntityFrameworkCore;
using Rihla.Data;
using Rihla.Models.Db;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Rihla.Services.Db;

public class PassportDataRepository : IPassportDataRepository
{
    private readonly AppDbContext _db;

    public PassportDataRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<DbPassportData?> GetByPassportNumberAsync(string passportNumber) =>
        _db.PassportData
           .Include(p => p.UploadedByUser)
           .FirstOrDefaultAsync(p => p.PassportNumber == passportNumber);

    public async Task AddAsync(DbPassportData passportData)
    {
        _db.PassportData.Add(passportData);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(DbPassportData passportData)
    {
        _db.PassportData.Update(passportData);
        await _db.SaveChangesAsync();
    }

    public async Task<List<DbPassportData>> GetAllAsync(string? status = null)
    {
        var query = _db.PassportData.Include(p => p.UploadedByUser).AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<PassportStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(p => p.Status == parsedStatus);
        }

        return await query.OrderByDescending(p => p.ExtractedAt).ToListAsync();
    }
}
