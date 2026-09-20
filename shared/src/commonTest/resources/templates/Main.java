package com.example.glance;

import java.util.List;

public class Main {
    private static final int VERSION = 1;

    static class Config {
        private final String name;
        private final int version;

        Config(String name, int version) {
            this.name = name;
            this.version = version;
        }

        @Override
        public String toString() {
            return "Config{" + name + " v" + version + "}";
        }
    }

    public static void main(String[] args) {
        Config config = new Config("Glance", VERSION);
        List<String> names = List.of(config.name);
        System.out.println(config);
    }
}
