package com.example.glance.Models

//Auth
data class LoginReq(
    val username: String,
    val password: String,
)

data class RegisterReq(
    val username: String,
    val password: String,
    val email: String? = null,
    val bio: String? = null,
)

data class UpdateUserReq(
    val username: String? = null,
    val email: String? = null,
    val bio: String? = null,
)

data class UserProfile(
    val publicId: String,
    val username: String,
    val email: String?,
    val bio: String?,
)

//Conv
data class LastMsgInfo(
    val msgId: Long,
    val content: String,
    val fromUid: Long,
    val ts: Long,
    val isRecalled: Boolean,
)

data class ConvItem(
    val convId: String,
    val name: String,
    val type: String,           // "private" | "group"
    val unreadCount: Int,
    val lastMsg: LastMsgInfo? = null,
    val username: String? = null,
    val publicId: String? = null,
    val groupId: String? = null,
    val memberCount: Int? = null,
)

data class ConvListRes(
    val convs: List<ConvItem>,
    val total: Int,
)

data class SendMsgReq(
    val convId: String,
    val content: String,
    val contentType: String = "text",
    val clientMsgId: String? = null,
    val text: String? = null,
)

data class Msg(
    val msgId: Long,
    val convId: String,
    val senderId: Long,
    val content: String,
    val contentType: String,
    val timestamp: Long,
    val isRecalled: Boolean,
    val artifact: BackendArtifact? = null,
)

//Friend
data class FriendReq(
    val requestId: Long,
    val fromUser: String? = null,
    val toUser: String? = null,
    val status: String,      // "pending" | "accepted" | "rejected"
    val createdAt: Long? = null,
)

//Groups
data class CreateGroupReq(
    val name: String,
    val description: String? = null,
)

data class UpdateGroupReq(
    val name: String? = null,
)

data class GroupInfo(
    val id: String,
    val name: String,
    val description: String?,
    val ownerId: Long,
    val memberCount: Int,
    val createdAt: Long,
)

//Files
enum class PresignOpn {
    Upload,
    Download,
}

data class PresignReq(
    val operation: PresignOpn,
    val convId: String? = null,
    val fileKey: String? = null,
    val fileExt: String? = null,
)

//Common Res
data class SuccessRes(
    val success: Boolean,
    val message: String? = null,
)

data class ErrorRes(
    val error: String,
)