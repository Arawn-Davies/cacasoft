/// The CLL/RET return-address stack: a fixed 255-deep array, entirely
/// separate from the SP-indexed byte data stack (PSH/POP). Per-VM instance
/// here (not static/process-wide like the original CallStack.cs was before
/// its own fix) -- see that fix's own commit for why static state there was
/// a real bug (state leaking across VM instances by run order), which this
/// port avoids by construction rather than needing the same fix applied
/// twice.
///
/// Call()/returnAddress() already reflect the off-by-one fix from that same
/// commit: Call writes at index then increments; returnAddress decrements
/// THEN reads, matching "index is a count of pushed entries, the top is at
/// index-1, not index."
public final class CallStack {
    private static let maxDepth = 255
    private var locations = [Int](repeating: 0, count: maxDepth)
    private var index = 0

    public init() {}

    public func call(_ location: Int) throws {
        guard index < CallStack.maxDepth else {
            throw CacaVMError.callStackOverflow(maxDepth: CallStack.maxDepth)
        }
        locations[index] = location
        index += 1
    }

    public func returnAddress() throws -> Int {
        guard index > 0 else {
            throw CacaVMError.callStackUnderflow
        }
        index -= 1
        let r = locations[index]
        locations[index] = 0
        return r
    }
}
