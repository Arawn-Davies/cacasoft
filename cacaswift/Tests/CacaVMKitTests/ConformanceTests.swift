import XCTest
@testable import CacaVMKit

/// Runs CacaVMKit against the shared, engine-agnostic corpus in
/// CacaVM/conformance/cases — see docs/CIL-Conformance-Suite.md. The C# engine
/// (CacaVM/source/Caca.VM.Tests) runs the exact same corpus via its own
/// adapter; a case only counts once both agree on it.
final class ConformanceTests: XCTestCase {
    private static var conformanceCasesDirectory: URL {
        // #filePath is this source file's own path at compile time; walk up
        // from it to the repo root (cacaswift/Tests/CacaVMKitTests/ -> repo
        // root) rather than relying on the test bundle's working directory,
        // which SPM does not guarantee points anywhere useful.
        URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent() // CacaVMKitTests
            .deletingLastPathComponent() // Tests
            .deletingLastPathComponent() // cacaswift (repo root)
            .appendingPathComponent("CacaVM/conformance/cases")
    }

    private static func discoverCases() -> [String] {
        let fm = FileManager.default
        guard let names = try? fm.contentsOfDirectory(atPath: conformanceCasesDirectory.path) else {
            return []
        }
        return names.sorted()
    }

    func testConformanceCorpus() throws {
        let cases = Self.discoverCases()
        XCTAssertFalse(cases.isEmpty, "no conformance cases found at \(Self.conformanceCasesDirectory.path)")

        var failures: [String] = []

        for name in cases {
            let dir = Self.conformanceCasesDirectory.appendingPathComponent(name)
            do {
                try runCase(at: dir)
            } catch {
                failures.append("\(name): \(error)")
            }
        }

        XCTAssertTrue(failures.isEmpty, "Conformance failures:\n" + failures.joined(separator: "\n"))
    }

    private func runCase(at dir: URL) throws {
        let source = try String(contentsOf: dir.appendingPathComponent("source.cil"), encoding: .utf8)

        let stdinPath = dir.appendingPathComponent("stdin.txt")
        let input: [String] = (try? String(contentsOf: stdinPath, encoding: .utf8))
            .map { $0.split(separator: "\n", omittingEmptySubsequences: false).map(String.init) }
            ?? []

        let ramSizePath = dir.appendingPathComponent("ramsize.txt")
        let ramSize = (try? String(contentsOf: ramSizePath, encoding: .utf8))
            .flatMap { Int($0.trimmingCharacters(in: .whitespacesAndNewlines)) }
            ?? 1_048_576

        let expectedStdoutPath = dir.appendingPathComponent("stdout.txt")
        let expectedErrorPath = dir.appendingPathComponent("error.txt")
        let expectedStdout = try? String(contentsOf: expectedStdoutPath, encoding: .utf8)
        let expectedError = try? String(contentsOf: expectedErrorPath, encoding: .utf8)

        precondition(
            (expectedStdout != nil) != (expectedError != nil),
            "\(dir.lastPathComponent): exactly one of stdout.txt/error.txt must be present"
        )

        let console = BufferConsole(input: input)

        do {
            let code = try Assembler.assemble(source)
            let vm = VM(program: code, ramSize: ramSize, console: console)
            try vm.run()

            if let expectedError {
                XCTFail("\(dir.lastPathComponent): expected an error containing '\(expectedError.trimmingCharacters(in: .whitespacesAndNewlines))', but it ran to completion with output: \(console.output)")
                return
            }

            XCTAssertEqual(console.output, expectedStdout, dir.lastPathComponent)
        } catch {
            guard let expectedError else {
                XCTFail("\(dir.lastPathComponent): unexpected error \(error) (output so far: \(console.output))")
                return
            }

            let message = "\(error)"
            XCTAssertTrue(
                message.contains(expectedError.trimmingCharacters(in: .whitespacesAndNewlines)),
                "\(dir.lastPathComponent): error '\(message)' did not contain expected substring '\(expectedError)'"
            )
        }
    }
}
