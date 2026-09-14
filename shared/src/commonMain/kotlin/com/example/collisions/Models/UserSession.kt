package com.example.glance.Models

data class UserSession(
    val publicId: String,
    val username: String,
    val token: String,
)