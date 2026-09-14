import os
import sys
from typing import List, Optional


class Greeter:
    """A simple greeter class."""

    def __init__(self, name: str, formal: bool = False):
        self.name = name
        self.formal = formal
        self._greetings: List[str] = []

    @property
    def greeting_count(self) -> int:
        return len(self._greetings)

    def greet(self, recipient: str) -> str:
        """Greet the recipient."""
        if self.formal:
            msg = f"Good day, {recipient}!"
        else:
            msg = f"Hello, {recipient}!"
        self._greetings.append(msg)
        return msg

    def farewell(self) -> str:
        return f"Farewell, {self.name}!"


def main() -> None:
    greeter = Greeter("World", formal=True)
    print(greeter.greet("World"))
    print(greeter.farewell())


if __name__ == "__main__":
    main()
