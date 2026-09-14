package main

import (
    "fmt"
    "os"
    "strings"
)

type Config struct {
    Name    string
    Version string
    Debug   bool
}

func NewConfig(name string, version string, debug bool) *Config {
    return &Config{
        Name:    name,
        Version: version,
        Debug:   debug,
    }
}

func (c *Config) String() string {
    return fmt.Sprintf("Config{%s v%s debug=%v}", c.Name, c.Version, c.Debug)
}

func main() {
    config := NewConfig("Collisions", "1.0.0", true)
    fmt.Println(config)

    args := os.Args[1:]
    if len(args) > 0 {
        joined := strings.Join(args, " ")
        fmt.Printf("Arguments: %s\n", joined)
    }
}
