using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Rihla.DTOs;

// ── Auth DTOs ─────────────────────────────────────────────────────────────────



public class LoginResponseDto
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_at")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("user")]
    public UserInfoDto User { get; set; } = new();
}

public class UserInfoDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("erp_next_user_id")]
    public string ErpNextUserId { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("primary_role")]
    public string PrimaryRole { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("allowed_modules")]
    public List<string> AllowedModules { get; set; } = [];

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "ar";
}





// ── Conversation DTOs ─────────────────────────────────────────────────────────

public class StartConversationRequestDto
{
    // No body required — conversation is tied to the authenticated user
}

public class StartConversationResponseDto
{
    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

public class SendMessageRequestDto
{
    [Required]
    [FromForm(Name = "conversation_id")]
    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    [FromForm(Name = "message")]
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [FromForm(Name = "file")]
    [JsonPropertyName("file")]
    public IFormFile? File { get; set; }
}

public class SendMessageResponseDto
{
    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("reply")]
    public string Reply { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("attachment_url")]
    public string? AttachmentUrl { get; set; }

    [JsonPropertyName("attachment_type")]
    public string? AttachmentType { get; set; }

    /// <summary>Populated only when a passport was detected — used for frontend confirmation UI.</summary>
    [JsonPropertyName("passport_data")]
    public PassportPreviewDto? PassportData { get; set; }
}

/// <summary>Lightweight read-only view of extracted passport data sent to the client for confirmation.</summary>
public class PassportPreviewDto
{
    [JsonPropertyName("passport_number")]  public string?  PassportNumber  { get; set; }
    [JsonPropertyName("full_name")]         public string?  FullName        { get; set; }
    [JsonPropertyName("full_name_ar")]      public string?  FullNameAr      { get; set; }
    [JsonPropertyName("nationality")]       public string?  Nationality     { get; set; }
    [JsonPropertyName("nationality_ar")]    public string?  NationalityAr   { get; set; }
    [JsonPropertyName("date_of_birth")]     public string?  DateOfBirth     { get; set; }
    [JsonPropertyName("expiry_date")]       public string?  ExpiryDate      { get; set; }
    [JsonPropertyName("date_of_issue")]     public string?  DateOfIssue     { get; set; }
    [JsonPropertyName("issuing_country")]   public string?  IssuingCountry  { get; set; }
    [JsonPropertyName("gender")]            public string?  Gender          { get; set; }
    [JsonPropertyName("gender_ar")]         public string?  GenderAr        { get; set; }
    [JsonPropertyName("place_of_birth")]    public string?  PlaceOfBirth    { get; set; }
    [JsonPropertyName("place_of_birth_ar")] public string?  PlaceOfBirthAr  { get; set; }
    [JsonPropertyName("profession")]        public string?  Profession      { get; set; }
    [JsonPropertyName("profession_ar")]     public string?  ProfessionAr    { get; set; }
    [JsonPropertyName("national_id")]       public string?  NationalId      { get; set; }
    [JsonPropertyName("address")]           public string?  Address         { get; set; }
    [JsonPropertyName("military_status")]   public string?  MilitaryStatus  { get; set; }
    [JsonPropertyName("image_url")]         public string?  ImageUrl        { get; set; }
    [JsonPropertyName("status")]            public string?  Status          { get; set; }
}

// ── Passport Confirmation DTOs ────────────────────────────────────────────────

public class PassportConfirmRequestDto
{
    [Required]
    [JsonPropertyName("passport_number")]
    public string PassportNumber { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("status")]
    public Rihla.Models.Db.PassportStatus Status { get; set; }
}

public class PassportConfirmResponseDto
{
    [JsonPropertyName("passport_number")]
    public string PassportNumber { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "confirmed";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "تم حفظ بيانات جواز السفر بنجاح.";
}

public class EndConversationRequestDto
{
    [Required]
    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; } = string.Empty;
}

public class GetConversationsRequestDto
{
    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("page_size")]
    public int PageSize { get; set; } = 20;
}

public class ConversationSummaryDto
{
    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("last_message")]
    public string? LastMessage { get; set; }

    [JsonPropertyName("last_message_at")]
    public DateTime? LastMessageAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("ended_at")]
    public DateTime? EndedAt { get; set; }
}

// ── Message DTOs ──────────────────────────────────────────────────────────────

public class MessageDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("attachment_url")]
    public string? AttachmentUrl { get; set; }

    [JsonPropertyName("attachment_type")]
    public string? AttachmentType { get; set; }
}

public class GetMessagesResponseDto
{
    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<MessageDto> Messages { get; set; } = [];
}

// ── Passport Extraction DTO ──────────────────────────────────────────────────

public class PassportExtractionResult
{
    [JsonPropertyName("is_passport")]
    public bool IsPassport { get; set; }

    [JsonPropertyName("is_clear")]
    public bool IsClear { get; set; }

    [JsonPropertyName("passport_number")]
    public string? PassportNumber { get; set; }

    // ── English Fields ──────────────────────────────────────────────────────
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("nationality")]
    public string? Nationality { get; set; }

    [JsonPropertyName("date_of_birth")]
    public string? DateOfBirth { get; set; }

    [JsonPropertyName("expiry_date")]
    public string? ExpiryDate { get; set; }

    [JsonPropertyName("date_of_issue")]
    public string? DateOfIssue { get; set; }

    [JsonPropertyName("issuing_country")]
    public string? IssuingCountry { get; set; }

    [JsonPropertyName("gender")]
    public string? Gender { get; set; }

    [JsonPropertyName("place_of_birth")]
    public string? PlaceOfBirth { get; set; }

    [JsonPropertyName("profession")]
    public string? Profession { get; set; }

    // ── Arabic Fields ───────────────────────────────────────────────────────
    [JsonPropertyName("full_name_ar")]
    public string? FullNameAr { get; set; }

    [JsonPropertyName("nationality_ar")]
    public string? NationalityAr { get; set; }

    [JsonPropertyName("gender_ar")]
    public string? GenderAr { get; set; }

    [JsonPropertyName("place_of_birth_ar")]
    public string? PlaceOfBirthAr { get; set; }

    [JsonPropertyName("profession_ar")]
    public string? ProfessionAr { get; set; }

    // ── Egyptian Passport-Specific ──────────────────────────────────────────
    [JsonPropertyName("national_id")]
    public string? NationalId { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("military_status")]
    public string? MilitaryStatus { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }
}

