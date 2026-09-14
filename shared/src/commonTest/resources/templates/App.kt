package com.example.app

data class User(
    val id: Long,
    val name: String,
    val email: String,
    val active: Boolean = true,
)

object UserRepository {
    private val users = mutableListOf<User>()

    fun add(user: User) {
        users.add(user)
    }

    fun find(id: Long): User? = users.firstOrNull { it.id == id }

    companion object {
        const val VERSION: String = "1.0"
    }
}

fun main() {
    val user = User(1, "Alice", "alice@example.com")
    UserRepository.add(user)
    val found = UserRepository.find(1)
    println(found?.name)
}
