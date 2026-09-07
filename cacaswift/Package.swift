// swift-tools-version: 5.10
import PackageDescription

let package = Package(
    name: "CacaStudioMac",
    platforms: [.macOS(.v14)], // needs dismissWindow (Window(id:)'s programmatic close), macOS 14+
    products: [
        .library(name: "CacaVMKit", targets: ["CacaVMKit"]),
        .executable(name: "CacaStudioMac", targets: ["CacaStudioMac"]),
    ],
    targets: [
        // A from-scratch Swift port of Caca.VM (CacaVM/source/Caca.VM) --
        // the register machine, RAM, the two-pass CIL
        // assembler, and the KEI/SWI standard library. Ported by hand against
        // the C# source and its own test suite, not machine-translated:
        // op.cs's exact semantics (including where they disagree with the
        // wiki, e.g. MOM/MOE's literal-only "direct" addressing, PSH/POP
        // being one byte at a time, no MOD opcode) are what this matches,
        // not the spec in isolation.
        .target(name: "CacaVMKit"),
        .executableTarget(
            name: "CacaStudioMac",
            dependencies: ["CacaVMKit"]
        ),
        .testTarget(
            name: "CacaVMKitTests",
            dependencies: ["CacaVMKit"]
        ),
    ]
)
