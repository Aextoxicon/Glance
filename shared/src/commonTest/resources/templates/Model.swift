import Foundation

struct User: Codable {
    let id: Int
    var name: String
    var email: String?
}

final class UserRepository {
    private var users: [User] = []

    func add(_ user: User) {
        users.append(user)
    }

    func find(id: Int) -> User? {
        users.first { $0.id == id }
    }

    var count: Int {
        users.count
    }
}
