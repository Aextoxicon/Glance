CC := gcc
CFLAGS := -Wall -O2
TARGET := glance

all: $(TARGET)

$(TARGET): main.c config.h
	$(CC) $(CFLAGS) -o $@ $^

test: $(TARGET)
	./$(TARGET) --selftest

clean:
	rm -f $(TARGET)

.PHONY: all test clean
