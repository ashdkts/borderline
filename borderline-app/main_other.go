//go:build !windows

package main

import "fmt"

func main() {
	fmt.Println("Borderline runs on Windows only.")
}
