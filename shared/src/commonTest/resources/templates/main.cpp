#include <iostream>
#include <string>
#include <vector>

namespace glance {

class Config {
public:
    Config(std::string name, int version) : name_(std::move(name)), version_(version) {}

    std::string ToString() const {
        return "Config{" + name_ + " v" + std::to_string(version_) + "}";
    }

private:
    std::string name_;
    int version_;
};

std::vector<std::string> Collect(const Config &config) {
    std::vector<std::string> items;
    items.push_back(config.ToString());
    return items;
}

}  // namespace glance

int main() {
    glance::Config config("Glance", 1);
    std::cout << config.ToString() << std::endl;
    return 0;
}
