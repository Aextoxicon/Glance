use std::collections::HashMap;

#[derive(Debug, Clone)]
struct User {
    id: u64,
    name: String,
    email: String,
}

impl User {
    fn new(id: u64, name: &str, email: &str) -> Self {
        Self {
            id,
            name: name.to_string(),
            email: email.to_string(),
        }
    }
}

struct UserRepository {
    users: HashMap<u64, User>,
}

impl UserRepository {
    fn new() -> Self {
        Self { users: HashMap::new() }
    }

    fn insert(&mut self, user: User) {
        self.users.insert(user.id, user);
    }

    fn get(&self, id: u64) -> Option<&User> {
        self.users.get(&id)
    }
}

fn main() {
    let mut repo = UserRepository::new();
    let user = User::new(1, "Alice", "alice@example.com");
    repo.insert(user);
    println!("{:?}", repo.get(1));
}
