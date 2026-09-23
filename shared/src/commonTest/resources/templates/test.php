<?php

namespace Glance;

const MAX_ITEMS = 100;

final class Registry
{
    private array $items = [];

    public function add(string $name, mixed $value): void
    {
        $this->items[$name] = $value;
    }

    public function lookup(string $name): mixed
    {
        return $this->items[$name] ?? null;
    }

    public function count(): int
    {
        return count($this->items);
    }

    public static function reset(): self
    {
        return new self();
    }
}

function main(): void
{
    $registry = Registry::reset();
    $registry->add("one", 1);
    echo $registry->count();
}
