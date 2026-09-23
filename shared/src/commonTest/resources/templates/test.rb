require "singleton"

module Glance
  MAX_ITEMS = 100
  EMPTY = []

  class Registry
    include Singleton

    def initialize
      @items = {}
    end

    def add(name, value)
      @items[name] = value
    end

    def lookup(pattern)
      @items.select { |key, _| key.match?(/#{pattern}/) }
    end

    def describe(name)
      <<~TEXT
        name: #{name}
        found: #{@items.key?(name)}
      TEXT
    end

    def self.reset!
      new_instance
    end
  end
end
