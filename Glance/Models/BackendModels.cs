namespace Glance.Models;

public record LoginReq(
    string Username,
    string Password
);

public record RegisterReq(
    string Username,
    string Password,
    string? Email = null,
    string? Bio = null
);

public record UpdateUserReq(
    string? Username = null,
    string? Email = null,
    string? Bio = null
);

public record UserProfile(
    string PublicId,
    string Username,
    string? Email,
    string? Bio
);

public record LastMsgInfo(
    long MsgId,
    string Content,
    long FromUid,
    long Ts,
    bool IsRecalled
);

public record ConvItem(
    string ConvId,
    string Name,
    string Type,
    int UnreadCount,
    LastMsgInfo? LastMsg = null,
    string? Username = null,
    string? PublicId = null,
    string? GroupId = null,
    int? MemberCount = null
);

public record ConvListRes(
    IReadOnlyList<ConvItem> Convs,
    int Total
);

public record SendMsgReq(
    string ConvId,
    string Content,
    string ContentType = "text",
    string? ClientMsgId = null,
    string? Text = null
);

public record Msg(
    long MsgId,
    string ConvId,
    long SenderId,
    string Content,
    string ContentType,
    long Timestamp,
    bool IsRecalled,
    BackendArtifact? Artifact = null
);

public record FriendReq(
    long RequestId,
    string? FromUser = null,
    string? ToUser = null,
    string Status,
    long? CreatedAt = null
);

public record CreateGroupReq(
    string Name,
    string? Description = null
);

public record UpdateGroupReq(
    string? Name = null
);

public record GroupInfo(
    string Id,
    string Name,
    string? Description,
    long OwnerId,
    int MemberCount,
    long CreatedAt
);

public enum PresignOpn
{
    Upload,
    Download,
}

public record PresignReq(
    PresignOpn Operation,
    string? ConvId = null,
    string? FileKey = null,
    string? FileExt = null
);

public record SuccessRes(
    bool Success,
    string? Message = null
);

public record ErrorRes(
    string Error
);
