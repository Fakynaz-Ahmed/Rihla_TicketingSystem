using Microsoft.Extensions.Logging;
using Rihla.DTOs;
using Rihla.Models.Db;
using Rihla.Services.Cache;
using Rihla.Services.Db;
using Rihla.Services.Storage;
using Rihla.WebSockets;
using System.Text.Json;
using System.IO;
using Microsoft.AspNetCore.Http;
using Rihla.Services.Tickets;
using Microsoft.Extensions.DependencyInjection;

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
    private readonly ITicketService _ticketService;
    private readonly IServiceScopeFactory _scopeFactory;
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
        ITicketService ticketService,
        IServiceScopeFactory scopeFactory,
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
        _ticketService = ticketService;
        _scopeFactory = scopeFactory;
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

        string userRole, language, status;
        if (cached is not null)
        {
            if (cached.UserId != userId)
                throw new UnauthorizedAccessException("هذه المحادثة ليست لك.");
            if (cached.Status == "ended")
                throw new InvalidOperationException("تمت إنهاء هذه المحادثة.");

            userRole = cached.UserRole;
            language = cached.Language;
            status   = cached.Status;
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
            status   = dbConv.Status;
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

        // Live support routing bypasses AI passport extraction and chat
        if (status != "active" && status != "ai")
        {
            var userMsgText = message ?? "";
            await Safe(() => _msgRepo.AddAsync(conversationId, "user", userMsgText, attachmentUrl, attachmentType));
            await HubSafe(() => PushMessageAsync(conversationId, "user", userMsgText, attachmentUrl, attachmentType));

            // Sync user message to ERPNext in background
            var syncText = string.IsNullOrEmpty(userMsgText) && !string.IsNullOrEmpty(attachmentUrl)
                ? "[أرسل مرفقاً]"
                : userMsgText;
            SyncMessageBackground(conversationId, "Customer", syncText);

            string systemReply = "";
            if (status == nameof(ConversationStatus.pending_cc))
            {
                systemReply = "[نظام] جميع موظفي الدعم مشغولون حالياً. يرجى الانتظار لحين تفرغ أحدهم.";
            }

            return new SendMessageResponseDto
            {
                ConversationId = conversationId,
                Reply          = systemReply,
                Timestamp      = DateTime.UtcNow,
                AttachmentUrl  = attachmentUrl,
                AttachmentType = attachmentType
            };
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

        if (dbConv.UserId != userId
            && dbConv.SupportId != userId
            && dbConv.SpecialistId != userId)
            throw new UnauthorizedAccessException("هذه المحادثة ليست لك.");

        var messages = await _msgRepo.GetByConversationAsync(conversationId, limit: 200);

        // Fetch linked ticket & visit IDs for frontend context
        var lastTicket = await _ticketService.GetLastTicketByConversationIdAsync(conversationId);
        var ticketId   = lastTicket?.Id;
        var visitId    = lastTicket?.VisitId
                         ?? await _ticketService.GetLastVisitIdByConversationIdAsync(conversationId);

        return new GetMessagesResponseDto
        {
            ConversationId = conversationId,
            Status         = dbConv.Status,
            TicketId       = ticketId,
            VisitId        = visitId,
            Messages       = messages.Select(m => new MessageDto
            {
                Id             = m.Id.ToString(),
                Role           = m.Role,
                Content        = m.Content,
                Timestamp      = m.Timestamp,
                AttachmentUrl  = m.AttachmentUrl,
                AttachmentType = m.AttachmentType
            }).ToList()
        };
    }

    // ── List conversations ────────────────────────────────────────────────────

    public async Task<PagedResult<ConversationSummaryDto>> GetConversationsAsync(
        int userId, int page, int pageSize)
    {
        var user = await _userRepo.GetByIdAsync(userId);
        var role = user?.PrimaryRole ?? "Customer";

        List<DbConversation> convs;
        int total;

        // Primary check: role-based routing
        // Fallback: even if PrimaryRole is not synced correctly from ERPNext,
        // check if the user has been assigned as support/specialist in any conversation.
        bool isAssignedAsSupport     = await _convRepo.IsAssignedAsSupportAsync(userId);
        bool isAssignedAsSpecialist  = await _convRepo.IsAssignedAsSpecialistAsync(userId);

        if (role == Rihla.Config.AppRoles.CustomerCare || role == Rihla.Config.AppRoles.Admin
            || (role == Rihla.Config.AppRoles.Employee && isAssignedAsSupport && !isAssignedAsSpecialist))
        {
            convs = await _convRepo.GetSupportConversationsAsync(userId, page, pageSize);
            total = await _convRepo.CountSupportConversationsAsync(userId);
        }
        else if (role == Rihla.Config.AppRoles.Specialist
            || (role == Rihla.Config.AppRoles.Employee && isAssignedAsSpecialist))
        {
            convs = await _convRepo.GetSpecialistConversationsAsync(userId, page, pageSize);
            total = await _convRepo.CountSpecialistConversationsAsync(userId);
        }
        else
        {
            convs = await _convRepo.GetByUserIdAsync(userId, page, pageSize);
            total = await _convRepo.CountByUserIdAsync(userId);
        }

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
                EndedAt        = c.EndedAt,
                SupportName    = c.SupportName,
                SpecialistName = c.SpecialistName,
                EscalationReason = c.EscalationReason,
                EscalatedAt    = c.EscalatedAt
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

    public async Task EscalateAsync(string conversationId, int userId, string reason)
    {
        var cachedJson = await _cache.GetAsync($"{ConvPrefix}{conversationId}");
        var cached = !string.IsNullOrEmpty(cachedJson)
            ? JsonSerializer.Deserialize<CachedConv>(cachedJson)
            : null;

        string userName, userRole, language;
        if (cached is not null)
        {
            if (cached.UserId != userId)
                throw new UnauthorizedAccessException("هذه المحادثة ليست لك.");
            if (cached.Status != "active" && cached.Status != "ai")
                throw new InvalidOperationException("لا يمكن تصعيد هذه المحادثة.");

            userRole = cached.UserRole;
            language = cached.Language;
            var u = await _userRepo.GetByIdAsync(userId);
            userName = u?.Name ?? "عميل";
        }
        else
        {
            var dbConv = await _convRepo.GetByIdAsync(conversationId)
                ?? throw new KeyNotFoundException("المحادثة غير موجودة.");

            if (dbConv.UserId != userId)
                throw new UnauthorizedAccessException("هذه المحادثة ليست لك.");
            if (dbConv.Status != "active" && dbConv.Status != "ai")
                throw new InvalidOperationException("لا يمكن تصعيد هذه المحادثة.");

            userName = dbConv.UserName;
            var u = await _userRepo.GetByIdAsync(userId);
            userRole = u?.PrimaryRole ?? "Customer";
            language = u?.Language ?? "ar";
        }

        var newStatus = nameof(ConversationStatus.pending_cc);
        await _convRepo.UpdateStatusExtendedAsync(conversationId, newStatus,
            escalationReason: reason, escalatedAt: DateTime.UtcNow);

        var updatedCached = new CachedConv
        {
            UserId = userId,
            UserRole = userRole,
            Language = language,
            Status = newStatus
        };
        await _cache.SetAsync($"{ConvPrefix}{conversationId}", JsonSerializer.Serialize(updatedCached), ConvTtl);

        var systemMsgText = $"[نظام] تم طلب تحويل المحادثة للدعم البشري. السبب: {reason}";
        await Safe(() => _msgRepo.AddAsync(conversationId, "system", systemMsgText));
        await HubSafe(() => PushMessageAsync(conversationId, "system", systemMsgText));

        await HubSafe(() => _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.StatusChanged,
            new StatusPayload(conversationId, newStatus)));

        _logger.LogInformation("Conversation {Id} escalated to Support. Reason: {Reason}", conversationId, reason);

        await AutoAssignSupportAsync(conversationId, userName, reason);
    }

    public async Task ReopenAsync(string conversationId, int userId)
    {
        var dbConv = await _convRepo.GetByIdAsync(conversationId)
            ?? throw new KeyNotFoundException("المحادثة غير موجودة.");

        if (dbConv.UserId != userId)
            throw new UnauthorizedAccessException("هذه المحادثة ليست لك.");
        if (dbConv.Status != "ended")
            throw new InvalidOperationException("المحادثة ليست مغلقة ليتم إعادة فتحها.");

        await _convRepo.ReopenConversationAsync(conversationId);

        var cached = new CachedConv
        {
            UserId = userId,
            UserRole = Rihla.Config.AppRoles.Customer,
            Language = dbConv.User?.Language ?? "ar",
            Status = "ai"
        };
        await _cache.SetAsync($"{ConvPrefix}{conversationId}", JsonSerializer.Serialize(cached), ConvTtl);

        var systemMsgText = "[نظام] تم إعادة فتح المحادثة وتوصيلها بالمساعد الذكي.";
        await Safe(() => _msgRepo.AddAsync(conversationId, "system", systemMsgText));
        await HubSafe(() => PushMessageAsync(conversationId, "system", systemMsgText));

        await HubSafe(() => _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.ConversationReopened,
            new ConversationReopenedPayload(conversationId)));
        await HubSafe(() => _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.StatusChanged,
            new StatusPayload(conversationId, "active")));

        _logger.LogInformation("Conversation {Id} reopened by user {UserId}.", conversationId, userId);
    }

    public async Task<SendMessageResponseDto> SupportSendMessageAsync(
        string conversationId, int supportUserId, string? message, IFormFile? file = null)
    {
        var dbConv = await _convRepo.GetByIdAsync(conversationId)
            ?? throw new KeyNotFoundException("المحادثة غير موجودة.");

        if (dbConv.SupportId != supportUserId)
            throw new UnauthorizedAccessException("أنت غير معين لهذه المحادثة.");

        if (dbConv.Status != nameof(ConversationStatus.with_cc) && dbConv.Status != nameof(ConversationStatus.pending_specialist) && dbConv.Status != nameof(ConversationStatus.with_specialist))
            throw new InvalidOperationException("لا يمكنك إرسال رسائل في هذه الحالة للمحادثة.");

        string? attachmentUrl = null;
        string? attachmentType = null;
        if (file != null)
        {
            attachmentType = file.ContentType;
            attachmentUrl = await _storage.SaveAttachmentFromFileAsync(file, conversationId);
        }

        var content = message ?? "";
        await Safe(() => _msgRepo.AddAsync(conversationId, "support", content, attachmentUrl, attachmentType));
        await HubSafe(() => PushMessageAsync(conversationId, "support", content, attachmentUrl, attachmentType));

        // Sync support message to ERPNext in background
        var syncText = string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(attachmentUrl)
            ? "[أرسل مرفقاً]"
            : content;
        SyncMessageBackground(conversationId, "Support", syncText);

        return new SendMessageResponseDto
        {
            ConversationId = conversationId,
            Reply          = content,
            Timestamp      = DateTime.UtcNow,
            AttachmentUrl  = attachmentUrl,
            AttachmentType = attachmentType
        };
    }

    public async Task EndBySupportAsync(string conversationId)
    {
        var dbConv = await _convRepo.GetByIdAsync(conversationId)
            ?? throw new KeyNotFoundException("المحادثة غير موجودة.");

        if (dbConv.Status == "ended")
            return;

        await _convRepo.UpdateStatusAsync(conversationId, nameof(ConversationStatus.ended), DateTime.UtcNow);

        var cachedJson = await _cache.GetAsync($"{ConvPrefix}{conversationId}");
        if (!string.IsNullOrEmpty(cachedJson))
        {
            var cached = JsonSerializer.Deserialize<CachedConv>(cachedJson);
            if (cached is not null)
            {
                cached.Status = nameof(ConversationStatus.ended);
                await _cache.SetAsync($"{ConvPrefix}{conversationId}", JsonSerializer.Serialize(cached), ConvTtl);
            }
        }

        var systemMsgText = "[نظام] تم إنهاء المحادثة من قبل الدعم.";
        await Safe(() => _msgRepo.AddAsync(conversationId, "system", systemMsgText));
        await HubSafe(() => PushMessageAsync(conversationId, "system", systemMsgText));

        await Safe(() => _ticketService.CloseTicketByConversationAsync(conversationId));

        await HubSafe(() => _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.ConversationEnded,
            new ConversationEndedPayload(conversationId)));
        await HubSafe(() => _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.StatusChanged,
            new StatusPayload(conversationId, nameof(ConversationStatus.ended))));

        _logger.LogInformation("Conversation {Id} ended by support.", conversationId);
    }

    public async Task RequestSpecialistAsync(string conversationId, int supportUserId, string description)
    {
        var dbConv = await _convRepo.GetByIdAsync(conversationId)
            ?? throw new KeyNotFoundException("المحادثة غير موجودة.");

        if (dbConv.SupportId != supportUserId)
            throw new UnauthorizedAccessException("أنت غير معين لهذه المحادثة لتطلب متخصصاً.");

        if (dbConv.Status != nameof(ConversationStatus.with_cc))
            throw new InvalidOperationException("يمكن طلب المتخصص فقط عندما تكون المحادثة مع الدعم الفني.");

        var specialist = await _ticketService.GetLeastBusySpecialistAsync()
            ?? throw new InvalidOperationException("لا يوجد متخصصون متاحون حالياً. يرجى المحاولة لاحقاً.");

        var specName = specialist.Name;
        var specId = specialist.Id;

        // 1. Create the ERPNext Task and Visit record first.
        // This is not wrapped in Safe() so any ERPNext/DB error here bubbles up and halts the request.
        var requestVisitDto = new RequestVisitDto
        {
            Description = description,
            Priority = "medium"
        };
        await _ticketService.CreateErpNextTaskForTicketAsync(conversationId, specId, requestVisitDto);

        // 2. Since ERPNext visit succeeded, proceed to update the conversation status, ticket specialist assignment and notify
        await _convRepo.UpdateStatusExtendedAsync(conversationId, nameof(ConversationStatus.with_specialist),
            specialistId: specId, specialistName: specName);

        var cachedJson = await _cache.GetAsync($"{ConvPrefix}{conversationId}");
        if (!string.IsNullOrEmpty(cachedJson))
        {
            var cached = JsonSerializer.Deserialize<CachedConv>(cachedJson);
            if (cached is not null)
            {
                cached.Status = nameof(ConversationStatus.with_specialist);
                await _cache.SetAsync($"{ConvPrefix}{conversationId}", JsonSerializer.Serialize(cached), ConvTtl);
            }
        }

        var specJoinedMsg = $"[نظام] انضم المتخصص {specName} للمحادثة لمتابعة طلبك.";
        await Safe(() => _msgRepo.AddAsync(conversationId, "system", specJoinedMsg));
        await HubSafe(() => PushMessageAsync(conversationId, "system", specJoinedMsg));

        await Safe(() => _ticketService.UpdateTicketSpecialistAsync(conversationId, specId, specName));

        _hub.AddUserToGroup(specId.ToString(), WsGroups.Conv(conversationId));

        await HubSafe(() => _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.AgentJoined,
            new AgentJoinedPayload(conversationId, specName, "specialist")));

        await HubSafe(() => _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.StatusChanged,
            new StatusPayload(conversationId, nameof(ConversationStatus.with_specialist))));

        _logger.LogInformation("Specialist {Name} (ID {Id}) assigned to conversation {ConvId}.", specName, specId, conversationId);
    }

    public async Task<SendMessageResponseDto> SpecialistSendMessageAsync(
        string conversationId, int specialistUserId, string? message, IFormFile? file = null)
    {
        var dbConv = await _convRepo.GetByIdAsync(conversationId)
            ?? throw new KeyNotFoundException("المحادثة غير موجودة.");

        if (dbConv.SpecialistId != specialistUserId)
            throw new UnauthorizedAccessException("أنت غير معين لهذه المحادثة كمتخصص.");

        if (dbConv.Status != nameof(ConversationStatus.with_specialist))
            throw new InvalidOperationException("لا يمكنك إرسال رسائل في هذه الحالة للمحادثة.");

        string? attachmentUrl = null;
        string? attachmentType = null;
        if (file != null)
        {
            attachmentType = file.ContentType;
            attachmentUrl = await _storage.SaveAttachmentFromFileAsync(file, conversationId);
        }

        var content = message ?? "";
        await Safe(() => _msgRepo.AddAsync(conversationId, "specialist", content, attachmentUrl, attachmentType));
        await HubSafe(() => PushMessageAsync(conversationId, "specialist", content, attachmentUrl, attachmentType));

        // Sync specialist message to ERPNext in background
        var syncText = string.IsNullOrEmpty(content) && !string.IsNullOrEmpty(attachmentUrl)
            ? "[أرسل مرفقاً]"
            : content;
        SyncMessageBackground(conversationId, "Specialist", syncText);

        return new SendMessageResponseDto
        {
            ConversationId = conversationId,
            Reply          = content,
            Timestamp      = DateTime.UtcNow,
            AttachmentUrl  = attachmentUrl,
            AttachmentType = attachmentType
        };
    }

    private async Task AutoAssignSupportAsync(string conversationId, string userName, string reason)
    {
        try
        {
            var ccAgents = await _userRepo.GetByPrimaryRoleAsync(Rihla.Config.AppRoles.CustomerCare);
            if (ccAgents.Count == 0)
            {
                _logger.LogWarning("No Customer Care agents found. Conversation {ConvId} stays pending.", conversationId);
                var busyMsg = "[نظام] جميع موظفي الدعم مشغولون حالياً. يرجى الانتظار لحين تفرغ أحدهم.";
                await Safe(() => _msgRepo.AddAsync(conversationId, "system", busyMsg));
                await HubSafe(() => PushMessageAsync(conversationId, "system", busyMsg));
                return;
            }

            var agentIds = ccAgents.Select(a => a.Id).ToList();
            var activeCounts = await _convRepo.GetActiveCountBySupportIdsAsync(agentIds);
            var bestAgent = ccAgents.MinBy(a => activeCounts.GetValueOrDefault(a.Id, 0));

            if (bestAgent is null) return;

            var agentName = bestAgent.Name;
            var agentId = bestAgent.Id;

            // ── Create ticket in ERPNext and link to conversation ─────────────
            // We must create the ticket FIRST before updating the conversation status
            var conv = await _convRepo.GetByIdAsync(conversationId);
            if (conv is not null)
            {
                var customer = await _userRepo.GetByIdAsync(conv.UserId);
                var customerErpId = customer?.ErpNextUserId ?? conv.UserName;

                // Do not use Safe() here. If local ticket creation fails, we should abort the assignment.
                await _ticketService.CreateEscalationTicketAsync(
                    conversationId   : conversationId,
                    customerErpNextUserId : customerErpId,
                    customerName     : conv.UserName,
                    supportAppUserId : agentId,
                    supportName      : agentName,
                    escalationReason : reason);
            }
            else
            {
                throw new KeyNotFoundException("المحادثة غير موجودة.");
            }

            await _convRepo.UpdateStatusExtendedAsync(conversationId, nameof(ConversationStatus.with_cc),
                supportId: agentId, supportName: agentName);

            var cachedJson = await _cache.GetAsync($"{ConvPrefix}{conversationId}");
            if (!string.IsNullOrEmpty(cachedJson))
            {
                var cached = JsonSerializer.Deserialize<CachedConv>(cachedJson);
                if (cached is not null)
                {
                    cached.Status = nameof(ConversationStatus.with_cc);
                    await _cache.SetAsync($"{ConvPrefix}{conversationId}", JsonSerializer.Serialize(cached), ConvTtl);
                }
            }

            var joinedMsg = $"[نظام] انضم موظف الدعم {agentName} للمحادثة لتلقي طلبك.";
            await Safe(() => _msgRepo.AddAsync(conversationId, "system", joinedMsg));
            await HubSafe(() => PushMessageAsync(conversationId, "system", joinedMsg));

            _hub.AddUserToGroup(agentId.ToString(), WsGroups.Conv(conversationId));

            await HubSafe(() => _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.AgentJoined,
                new AgentJoinedPayload(conversationId, agentName, "customer_care")));

            await HubSafe(() => _hub.SendToGroupAsync(WsGroups.Conv(conversationId), WsEvents.StatusChanged,
                new StatusPayload(conversationId, nameof(ConversationStatus.with_cc))));

            _logger.LogInformation("Support Agent {Name} (ID {Id}) assigned to conversation {ConvId}.", agentName, agentId, conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AutoAssignSupportAsync failed for conversation {ConvId}", conversationId);
            // If it fails, we should notify the user or revert status, but since it's an auto-assignment, 
            // the status is left as pending_cc (which is handled by EscalateAsync before this).
        }
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

    private void SyncMessageBackground(string conversationId, string senderRole, string content)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ticketService = scope.ServiceProvider.GetRequiredService<ITicketService>();
                await ticketService.SyncMessageToErpNextAsync(conversationId, senderRole, content);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background message sync failed for conversation {ConvId}.", conversationId);
            }
        });
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
