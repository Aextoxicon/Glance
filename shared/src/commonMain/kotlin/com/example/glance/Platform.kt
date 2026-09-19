package com.example.glance

interface Platform {
    val name: String
}

expect fun getPlatform(): Platform