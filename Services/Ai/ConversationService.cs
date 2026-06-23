using Microsoft.Extensions.Logging;
using Rihla.DTOs;
using Rihla.Services.Cache;
using Rihla.Services.Db;
using Rihla.Services.Storage;
using Rihla.Models.Db;
using Rihla.WebSockets;
using System.Text.Json;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace Rihla.Services.Ai;

/// <summary>
/// Conversation state machine (simplified):
///
///   active → ended
///
/// Messages are persisted to MongoDB.
/// Redis caches conversation metadata (24h TTL).
/// WebSocket hub pushes messages in real-time.
/// </summary>
public class ConversationService : IConversationService
{
    private readonly IAiService _ai;
    private readonly ICacheService _cache;
    private readonly IConversationRepository _convRepo;
    private readonly IMessageRepository _msgRepo;
    private readonly IAppUserRepository _userRepo;
    private readonly IWebSocketHub _hub;
    private readonly IStorageService _storage;
    private readonly IPassportDataRepository _passportRepo;
    private readonly ILogger<ConversationService> _logger;

    private static readonly TimeSpan ConvTtl = TimeSpan.FromHours(48);
    private const string ConvPrefix = "conv:";

    public ConversationService(
        IAiService ai,
        ICacheService cache,
        IConversationRepository convRepo,
        IMessageRepository msgRepo,
        IAppUserRepository userRepo,
        IWebSocketHub hub,
        IStorageService storage,
        IPassportDataRepository passportRepo,
        ILogger<ConversationService> logger)
    {
        _ai       = ai;
        _cache    = cache;
        _convRepo = convRepo;
        _msgRepo  = msgRepo;
        _userRepo = userRepo;
        _hub      = hub;
        _storage  = storage;
        _passportRepo = passportRepo;
        _logger   = logger;
    }

    // ── Start ─────────────────────────────────────────────────────────────────

    public async Task<StartConversationResponseDto> StartAsync(
        int userId, string userName, string userRole, string language)
    {
        var conversationId = Guid.NewGuid().ToString("N");

        await _convRepo.CreateAsync(conversationId, userId, userName);

        // Cache conversation metadata
        await _cache.SetAsync(
            $"{ConvPrefix}{conversationId}",
            JsonSerializer.Serialize(new CachedConv { UserId = userId, UserRole = userRole, Language = language, Status = "active" }),
            ConvTtl);

        // Join the user to the WS group
        _hub.AddUserToGroup(userId.ToString(), WsGroups.Conv(conversationId));

        _logger.LogInformation("Conversation {Id} started by user {UserId}.", conversationId, userId);

        return new StartConversationResponseDto
        {
            ConversationId = conversationId,
            CreatedAt      = DateTime.UtcNow
        };
    }

    // ── Send Message ──────────────────────────────────────────────────────────

    public async Task<SendMessageResponseDto> SendMessageAsync(
        string conversationId, int userId, string? message, IFormFile? file = null)
    {
        // Load cached state
        var cachedJson = await _cache.GetAsync($"{ConvPrefix}{conversationId}");
        var cached = !string.IsNullOrEmpty(cachedJson)
            ? JsonSerializer.Deserialize<CachedConv>(cachedJson)
            : null;

        string userRole, language;
        if (cached is not null)
        {
            if (cached.UserId != userId)
                throw new UnauthorizedAccessException("هذه المحادثة ليست لك.");
            if (cached.Status == "ended")
                throw new InvalidOperationException("تمت إنهاء هذه المحادثة.");

            userRole = cached.UserRole;
            language = cached.Language;
        }
        else
        {
            // Fallback to DB
            var dbConv = await _convRepo.GetByIdAsync(conversationId)
                ?? throw new KeyNotFoundException("المحادثة غير موجودة.");

            if (dbConv.UserId != userId)
                throw new UnauthorizedAccessException("هذه المحادثة ليست لك.");
            if (dbConv.Status == "ended")
                throw new InvalidOperationException("تمت إنهاء هذه المحادثة.");

            var user = await _userRepo.GetByIdAsync(userId);
            userRole = user?.PrimaryRole ?? "Employee";
            language = user?.Language ?? "ar";
        }

        string? attachmentUrl = null;
        string? attachmentType = null;
        byte[]? fileBytes = null;

        if (file != null)
        {
            attachmentType = file.ContentType;
            attachmentUrl = await _storage.SaveAttachmentFromFileAsync(file, conversationId);

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            fileBytes = ms.ToArray();
        }

        // Check if file is uploaded
        if (file != null)
        {
            if (fileBytes == null || !file.ContentType.StartsWith("image/"))
            {
                var fileUserContent = "أرسل ملفاً غير مدعوم";
                await Safe(() => _msgRepo.AddAsync(conversationId, "user", fileUserContent, attachmentUrl, attachmentType));
                await HubSafe(() => PushMessageAsync(conversationId, "user", fileUserContent, attachmentUrl, attachmentType));

                var aiReply = "عذراً، يرجى رفع صورة جواز سفر صالحة وواضحة فقط (تنسيق JPG أو PNG).";
                await Safe(() => _msgRepo.AddAsync(conversationId, "ai", aiReply));
                await HubSafe(() => PushMessageAsync(conversationId, "ai", aiReply));

                return new SendMessageResponseDto
                {
                    ConversationId = conversationId,
                    Reply          = aiReply,
                    Timestamp      = DateTime.UtcNow,
                    AttachmentUrl  = attachmentUrl,
                    AttachmentType = attachmentType
                };
            }

            var extractionResult = await _ai.ExtractPassportDataAsync(fileBytes, file.ContentType);
            if (extractionResult != null && extractionResult.IsPassport)
            {
                if (extractionResult.IsClear && !string.IsNullOrWhiteSpace(extractionResult.PassportNumber))
                {
                    // Parse dates
                    DateTime? dob = null;
                    if (DateTime.TryParse(extractionResult.DateOfBirth, out var parsedDob)) dob = parsedDob;

                    DateTime? expiry = null;
                    if (DateTime.TryParse(extractionResult.ExpiryDate, out var parsedExpiry)) expiry = parsedExpiry;

                    DateTime? issue = null;
                    if (DateTime.TryParse(extractionResult.DateOfIssue, out var parsedIssue)) issue = parsedIssue;

                    // Parse MilitaryStatus enum
                    MilitaryStatus? military = null;
                    if (extractionResult.MilitaryStatus == "غير مطلوب للتجنيد") military = MilitaryStatus.NotRequired;
                    else if (extractionResult.MilitaryStatus == "في سن التجنيد") military = MilitaryStatus.SubjectToMilitaryAge;
                    else if (extractionResult.MilitaryStatus == "معافى مؤقت") military = MilitaryStatus.TemporarilyExempted;

                    // Save immediately to SQL DB as Pending
                    var existingPassport = await _passportRepo.GetByPassportNumberAsync(extractionResult.PassportNumber);
                    if (existingPassport != null)
                    {
                        existingPassport.UploadedByUserId = userId;
                        existingPassport.ConversationId   = conversationId;
                        // English
                        existingPassport.FullName         = extractionResult.FullName;
                        existingPassport.Nationality      = extractionResult.Nationality;
                        existingPassport.DateOfBirth      = dob;
                        existingPassport.ExpiryDate       = expiry;
                        existingPassport.DateOfIssue      = issue;
                        existingPassport.IssuingCountry   = extractionResult.IssuingCountry;
                        existingPassport.Gender           = extractionResult.Gender;
                        existingPassport.PlaceOfBirth     = extractionResult.PlaceOfBirth;
                        existingPassport.Profession       = extractionResult.Profession;
                        // Arabic
                        existingPassport.FullNameAr       = extractionResult.FullNameAr;
                        existingPassport.NationalityAr    = extractionResult.NationalityAr;
                        existingPassport.GenderAr         = extractionResult.GenderAr;
                        existingPassport.PlaceOfBirthAr   = extractionResult.PlaceOfBirthAr;
                        existingPassport.ProfessionAr     = extractionResult.ProfessionAr;
                        // Egyptian-specific
                        existingPassport.NationalId       = extractionResult.NationalId;
                        existingPassport.Address          = extractionResult.Address;
                        existingPassport.MilitaryStatus   = military;
                        // Meta
                        existingPassport.RawJsonData      = JsonSerializer.Serialize(extractionResult);
                        existingPassport.ImageUrl         = attachmentUrl;
                        existingPassport.Status           = PassportStatus.Pending;
                        existingPassport.ExtractedAt      = DateTime.UtcNow;

                        await _passportRepo.UpdateAsync(existingPassport);
                    }
                    else
                    {
                        var newPassport = new DbPassportData
                        {
                            PassportNumber   = extractionResult.PassportNumber,
                            UploadedByUserId = userId,
                            ConversationId   = conversationId,
                            // English
                            FullName         = extractionResult.FullName,
                            Nationality      = extractionResult.Nationality,
                            DateOfBirth      = dob,
                            ExpiryDate       = expiry,
                            DateOfIssue      = issue,
                            IssuingCountry   = extractionResult.IssuingCountry,
                            Gender           = extractionResult.Gender,
                            PlaceOfBirth     = extractionResult.PlaceOfBirth,
                            Profession       = extractionResult.Profession,
                            // Arabic
                            FullNameAr       = extractionResult.FullNameAr,
                            NationalityAr    = extractionResult.NationalityAr,
                            GenderAr         = extractionResult.GenderAr,
                            PlaceOfBirthAr   = extractionResult.PlaceOfBirthAr,
                            ProfessionAr     = extractionResult.ProfessionAr,
                            // Egyptian-specific
                            NationalId       = extractionResult.NationalId,
                            Address          = extractionResult.Address,
                            MilitaryStatus   = military,
                            // Meta
                            RawJsonData      = JsonSerializer.Serialize(extractionResult),
                            ImageUrl         = attachmentUrl,
                            Status           = PassportStatus.Pending,
                            ExtractedAt      = DateTime.UtcNow
                        };

                        await _passportRepo.AddAsync(newPassport);
                    }

                    // Save user message to MongoDB with attachment
                    var userMsgText = message ?? "";
                    await Safe(() => _msgRepo.AddAsync(conversationId, "user", userMsgText, attachmentUrl, attachmentType));
                    await HubSafe(() => PushMessageAsync(conversationId, "user", userMsgText, attachmentUrl, attachmentType));

                    // Build rich bilingual AI reply notifying the user that the passport is pending confirmation by the admin
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"✅ **تم استلام جواز السفر واستخراج البيانات بنجاح!**");
                    sb.AppendLine($"");
                    sb.AppendLine($"⏳ **حالة جواز السفر:** قيد المراجعة والتأكيد من قبل المسؤول.");
                    sb.AppendLine($"");
                    sb.AppendLine($"📋 **البيانات المستخرجة مبدئياً:**");
                    sb.AppendLine($"");
                    sb.AppendLine($"🔢 **رقم الجواز / Passport No.:** {extractionResult.PassportNumber}");
                    if (!string.IsNullOrEmpty(extractionResult.NationalId))
                        sb.AppendLine($"🪪 **الرقم القومي / National ID:** {extractionResult.NationalId}");
                    sb.AppendLine($"");
                    sb.AppendLine($"👤 **الاسم بالعربية:** {extractionResult.FullNameAr ?? "—"}");
                    sb.AppendLine($"👤 **Full Name (EN):** {extractionResult.FullName ?? "—"}");
                    sb.AppendLine($"");
                    sb.AppendLine($"🌍 **الجنسية:** {extractionResult.NationalityAr ?? extractionResult.Nationality ?? "—"}  |  **Nationality:** {extractionResult.Nationality ?? "—"}");
                    sb.AppendLine($"⚧ **الجنس:** {extractionResult.GenderAr ?? "—"}  |  **Sex:** {(extractionResult.Gender?.ToUpper() == "M" ? "Male" : extractionResult.Gender?.ToUpper() == "F" ? "Female" : extractionResult.Gender ?? "—")}");
                    sb.AppendLine($"🎂 **تاريخ الميلاد / DOB:** {extractionResult.DateOfBirth ?? "—"}");
                    sb.AppendLine($"📍 **مكان الميلاد:** {extractionResult.PlaceOfBirthAr ?? extractionResult.PlaceOfBirth ?? "—"}  |  **POB:** {extractionResult.PlaceOfBirth ?? "—"}");
                    sb.AppendLine($"");
                    sb.AppendLine($"📅 **تاريخ الإصدار / Issue Date:** {extractionResult.DateOfIssue ?? "—"}");
                    sb.AppendLine($"📅 **تاريخ الانتهاء / Expiry Date:** {extractionResult.ExpiryDate ?? "—"}");
                    sb.AppendLine($"🏛️ **جهة الإصدار / Issued By:** {extractionResult.IssuingCountry ?? "—"}");
                    if (!string.IsNullOrEmpty(extractionResult.Profession) || !string.IsNullOrEmpty(extractionResult.ProfessionAr))
                    {
                        sb.AppendLine($"");
                        sb.AppendLine($"💼 **المهنة:** {extractionResult.ProfessionAr ?? "—"}  |  **Profession:** {extractionResult.Profession ?? "—"}");
                    }
                    if (!string.IsNullOrEmpty(extractionResult.MilitaryStatus))
                        sb.AppendLine($"🪖 **الموقف التجنيدي:** {extractionResult.MilitaryStatus}");
                    if (!string.IsNullOrEmpty(extractionResult.Address))
                        sb.AppendLine($"🏠 **العنوان:** {extractionResult.Address}");

                    if (expiry.HasValue)
                    {
                        var now = DateTime.UtcNow;
                        if (expiry.Value < now)
                        {
                            sb.AppendLine($"");
                            sb.AppendLine($"❌ **تنبيه هام:** جواز السفر هذا منتهي الصلاحية بالفعل! يرجى العلم أنه لن يمكن استخدام هذا الجواز.");
                        }
                        else if (expiry.Value < now.AddMonths(6))
                        {
                            sb.AppendLine($"");
                            sb.AppendLine($"⚠️ **تنبيه هام:** صلاحية جواز السفر تنتهي خلال أقل من 6 أشهر! يرجى العلم أنه لن يمكن استخدام هذا الجواز.");
                        }
                    }

                    var aiReply = sb.ToString().TrimEnd();

                    await Safe(() => _msgRepo.AddAsync(conversationId, "ai", aiReply));
                    await HubSafe(() => PushMessageAsync(conversationId, "ai", aiReply));

                    // Return SendMessageResponseDto
                    var previewDto = new PassportPreviewDto
                    {
                        PassportNumber  = extractionResult.PassportNumber,
                        FullName        = extractionResult.FullName,
                        FullNameAr      = extractionResult.FullNameAr,
                        Nationality     = extractionResult.Nationality,
                        NationalityAr   = extractionResult.NationalityAr,
                        DateOfBirth     = extractionResult.DateOfBirth,
                        ExpiryDate      = extractionResult.ExpiryDate,
                        DateOfIssue     = extractionResult.DateOfIssue,
                        IssuingCountry  = extractionResult.IssuingCountry,
                        Gender          = extractionResult.Gender,
                        GenderAr        = extractionResult.GenderAr,
                        PlaceOfBirth    = extractionResult.PlaceOfBirth,
                        PlaceOfBirthAr  = extractionResult.PlaceOfBirthAr,
                        Profession      = extractionResult.Profession,
                        ProfessionAr    = extractionResult.ProfessionAr,
                        NationalId      = extractionResult.NationalId,
                        Address         = extractionResult.Address,
                        MilitaryStatus  = extractionResult.MilitaryStatus,
                        ImageUrl        = attachmentUrl,
                        Status          = PassportStatus.Pending.ToString().ToLower()
                    };

                    return new SendMessageResponseDto
                    {
                        ConversationId = conversationId,
                        Reply          = aiReply,
                        Timestamp      = DateTime.UtcNow,
                        AttachmentUrl  = attachmentUrl,
                        AttachmentType = attachmentType,
                        PassportData   = previewDto
                    };
                }
                else
                {
                    // Passport but unclear / wrong page
                    var userMsgText = message ?? "";
                    await Safe(() => _msgRepo.AddAsync(conversationId, "user", userMsgText, attachmentUrl, attachmentType));
                    await HubSafe(() => PushMessageAsync(conversationId, "user", userMsgText, attachmentUrl, attachmentType));

                    var aiReply = extractionResult.ErrorMessage ?? "الصورة غير واضحة، يرجى إعادة تصوير جواز السفر بشكل أوضح.";
                    await Safe(() => _msgRepo.AddAsync(conversationId, "ai", aiReply));
                    await HubSafe(() => PushMessageAsync(conversationId, "ai", aiReply));

                    return new SendMessageResponseDto
                    {
                        ConversationId = conversationId,
                        Reply          = aiReply,
                        Timestamp      = DateTime.UtcNow,
                        AttachmentUrl  = attachmentUrl,
                        AttachmentType = attachmentType
                    };
                }
            }
            else
            {
                var userMsgText = message ?? "";
                await Safe(() => _msgRepo.AddAsync(conversationId, "user", userMsgText, attachmentUrl, attachmentType));
                await HubSafe(() => PushMessageAsync(conversationId, "user", userMsgText, attachmentUrl, attachmentType));

                var aiReply = "عذراً، الصورة المرفوعة ليست جواز سفر. يرجى رفع صورة جواز سفر صالحة وواضحة فقط للبدء في استخراج البيانات.";
                await Safe(() => _msgRepo.AddAsync(conversationId, "ai", aiReply));
                await HubSafe(() => PushMessageAsync(conversationId, "ai", aiReply));

                return new SendMessageResponseDto
                {
                    ConversationId = conversationId,
                    Reply          = aiReply,
                    Timestamp      = DateTime.UtcNow,
                    AttachmentUrl  = attachmentUrl,
                    AttachmentType = attachmentType
                };
            }
        }

        // Default flow: text message or general image (brochure, etc.)
        var userContent = message ?? "";
        await Safe(() => _msgRepo.AddAsync(conversationId, "user", userContent, attachmentUrl, attachmentType));
        await HubSafe(() => PushMessageAsync(conversationId, "user", userContent, attachmentUrl, attachmentType));

        var history = await BuildHistoryAsync(conversationId);
        
        var reply = await _ai.ChatAsync(conversationId, message ?? "", history, userRole, language, fileBytes, attachmentType);

        if (!string.IsNullOrEmpty(reply))
        {
            await Safe(() => _msgRepo.AddAsync(conversationId, "ai", reply));
            await HubSafe(() => PushMessageAsync(conversationId, "ai", reply));
        }

        return new SendMessageResponseDto
        {
            ConversationId = conversationId,
            Reply          = reply,
            Timestamp      = DateTime.UtcNow,
            AttachmentUrl  = attachmentUrl,
            AttachmentType = attachmentType
        };
    }

    // ── End ───────────────────────────────────────────────────────────────────

    public async Task EndAsync(string conversationId, int userId)
    {
        var cachedJson = await _cache.GetAsync($"{ConvPrefix}{conversationId}");
        var cached = !string.IsNullOrEmpty(cachedJson)
            ? JsonSerializer.Deserialize<CachedConv>(cachedJson)
            : null;

        if (cached is not null)
        {
            if (cached.UserId != userId)
                throw new UnauthorizedAccessException("هذه المحادثة ليست لك.");

            cached.Status = "ended";
            await _cache.SetAsync($"{ConvPrefix}{conversationId}", JsonSerializer.Serialize(cached), ConvTtl);
        }
        else
        {
            var dbConv = await _convRepo.GetByIdAsync(conversationId)
                ?? throw new KeyNotFoundException("المحادثة غير موجودة.");

            if (dbConv.UserId != userId)
                throw new UnauthorizedAccessException("هذه المحادثة ليست لك.");
        }

        await _convRepo.UpdateStatusAsync(conversationId, "ended", DateTime.UtcNow);
        await Safe(() => _msgRepo.AddAsync(conversationId, "system", "تم إنهاء المحادثة."));

        await HubSafe(() => PushMessageAsync(conversationId, "system", "تم إنهاء المحادثة."));
        await HubSafe(() => _hub.SendToGroupAsync(
            WsGroups.Conv(conversationId), WsEvents.ConversationEnded,
            new ConversationEndedPayload(conversationId)));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task DeleteAsync(string conversationId)
    {
        await _cache.RemoveAsync($"{ConvPrefix}{conversationId}");
        await Task.WhenAll(
            _convRepo.DeleteAsync(conversationId),
            _msgRepo.DeleteByConversationAsync(conversationId));

        _logger.LogInformation("Conversation {Id} deleted.", conversationId);
    }

    // ── History ───────────────────────────────────────────────────────────────

    public async Task<GetMessagesResponseDto> GetHistoryAsync(string conversationId, int userId)
    {
        // Verify access
        var dbConv = await _convRepo.GetByIdAsync(conversationId)
            ?? throw new KeyNotFoundException("المحادثة غير موجودة.");

        if (dbConv.UserId != userId)
            throw new UnauthorizedAccessException("هذه المحادثة ليست لك.");

        var messages = await _msgRepo.GetByConversationAsync(conversationId, limit: 200);

        return new GetMessagesResponseDto
        {
            ConversationId = conversationId,
            Status         = dbConv.Status,
            Messages       = messages.Select(m => new MessageDto
            {
                Id          = m.Id.ToString(),
                Role        = m.Role,
                Content     = m.Content,
                Timestamp   = m.Timestamp,
                AttachmentUrl = m.AttachmentUrl,
                AttachmentType = m.AttachmentType
            }).ToList()
        };
    }

    // ── List conversations ────────────────────────────────────────────────────

    public async Task<PagedResult<ConversationSummaryDto>> GetConversationsAsync(
        int userId, int page, int pageSize)
    {
        var convs  = await _convRepo.GetByUserIdAsync(userId, page, pageSize);
        var total  = await _convRepo.CountByUserIdAsync(userId);

        // Get last message for each conversation
        var ids      = convs.Select(c => c.ConversationId).ToList();
        var messages = await _msgRepo.GetByConversationIdsAsync(ids);
        var lastMsgs = messages
            .GroupBy(m => m.ConversationId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(m => m.Timestamp).First());

        var items = convs.Select(c =>
        {
            lastMsgs.TryGetValue(c.ConversationId, out var last);
            return new ConversationSummaryDto
            {
                ConversationId = c.ConversationId,
                Title          = c.Title,
                Status         = c.Status,
                LastMessage    = last?.Content,
                LastMessageAt  = last?.Timestamp,
                CreatedAt      = c.CreatedAt,
                EndedAt        = c.EndedAt
            };
        }).ToList();

        return new PagedResult<ConversationSummaryDto>
        {
            Page     = page,
            PageSize = pageSize,
            Total    = total,
            Items    = items
        };
    }

    public async Task<PassportConfirmResponseDto> UpdatePassportStatusAsync(string passportNumber, PassportStatus newStatus, int userId)
    {
        if (newStatus == PassportStatus.Pending)
        {
            throw new InvalidOperationException("الحالة المطلوبة غير صالحة.");
        }

        var passport = await _passportRepo.GetByPassportNumberAsync(passportNumber);
        if (passport == null)
        {
            throw new KeyNotFoundException("لم يتم العثور على جواز سفر بهذا الرقم.");
        }

        passport.Status = newStatus;
        await _passportRepo.UpdateAsync(passport);

        // Save status change message to MongoDB chat history
        var statusTextAr = newStatus == PassportStatus.Confirmed ? "تأكيد" : newStatus == PassportStatus.Canceled ? "إلغاء/رفض" : "تعليق";
        var confirmationMsg = $"[نظام] تم {statusTextAr} بيانات جواز السفر رقم {passportNumber} من قبل المسؤول.";

        await Safe(() => _msgRepo.AddAsync(passport.ConversationId, "system", confirmationMsg));
        await HubSafe(() => PushMessageAsync(passport.ConversationId, "system", confirmationMsg));

        return new PassportConfirmResponseDto
        {
            PassportNumber = passport.PassportNumber,
            Status         = newStatus.ToString().ToLower(),
            Message        = $"تم تحديث حالة جواز السفر إلى {statusTextAr} بنجاح."
        };
    }

    public async Task<List<PassportPreviewDto>> GetPassportsAsync(string? status = null)
    {
        var list = await _passportRepo.GetAllAsync(status);
        return list.Select(p => new PassportPreviewDto
        {
            PassportNumber = p.PassportNumber,
            FullName       = p.FullName,
            FullNameAr     = p.FullNameAr,
            Nationality    = p.Nationality,
            NationalityAr  = p.NationalityAr,
            DateOfBirth    = p.DateOfBirth?.ToString("yyyy-MM-dd"),
            ExpiryDate     = p.ExpiryDate?.ToString("yyyy-MM-dd"),
            DateOfIssue    = p.DateOfIssue?.ToString("yyyy-MM-dd"),
            IssuingCountry = p.IssuingCountry,
            Gender         = p.Gender,
            GenderAr       = p.GenderAr,
            PlaceOfBirth   = p.PlaceOfBirth,
            PlaceOfBirthAr = p.PlaceOfBirthAr,
            Profession     = p.Profession,
            ProfessionAr   = p.ProfessionAr,
            NationalId     = p.NationalId,
            Address        = p.Address,
            MilitaryStatus = p.MilitaryStatus switch
            {
                MilitaryStatus.NotRequired => "غير مطلوب للتجنيد",
                MilitaryStatus.SubjectToMilitaryAge => "في سن التجنيد",
                MilitaryStatus.TemporarilyExempted => "معافى مؤقت",
                _ => null
            },
            ImageUrl       = p.ImageUrl,
            Status         = p.Status.ToString().ToLower()
        }).ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<(string Role, string Content)>> BuildHistoryAsync(string conversationId)
    {
        var msgs = await _msgRepo.GetByConversationAsync(conversationId, limit: 40);
        return msgs
            .Where(m => m.Role != "system")
            .Select(m => (m.Role, m.Content))
            .ToList();
    }

    private async Task PushMessageAsync(string conversationId, string role, string content, string? attachmentUrl = null, string? attachmentType = null)
    {
        await _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.MessageReceived,
            new MessagePayload(
                conversation_id : conversationId,
                id              : Guid.NewGuid().ToString("N"),
                role            : role,
                content         : content,
                timestamp       : DateTime.UtcNow,
                attachment_url  : attachmentUrl,
                attachment_type : attachmentType));
    }

    private async Task Safe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Non-critical DB operation failed."); }
    }

    private async Task HubSafe(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _logger.LogWarning(ex, "WebSocket push failed (non-critical)."); }
    }
}

/// <summary>Redis-cached conversation metadata.</summary>
file sealed class CachedConv
{
    public int    UserId   { get; set; }
    public string UserRole { get; set; } = string.Empty;
    public string Language { get; set; } = "ar";
    public string Status   { get; set; } = "active";
}
