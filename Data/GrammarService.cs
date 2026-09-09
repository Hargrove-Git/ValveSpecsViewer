using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace ValveSpecsViewer.Data;

/// Decodes a valve model number into its configuration-code segments using the versioned
/// grammar seeded from the manufacturer's catalog.
///
/// Why this isn't a simple split on '-': dashes in real Consolidated model numbers do NOT
/// line up with segment boundaries. `1905-00MC-1-CC-TD-34-RF-SS-HP` packs three segments into
/// its first token (valve type 19 + pressure class 05) and three more into its second
/// (bellows 00, orifice M, then an uninterpreted 'C'). So each dash-delimited token is walked
/// character by character, consuming as many consecutive segments as will match.
///
/// Anything that cannot be matched is reported rather than guessed — an honest "could not
/// interpret" beats a plausible-looking wrong decode.
public class GrammarService(ValveSpecsDb db)
{
    public Task<List<GrammarVersion>> GetVersionsAsync(int? familyId = null) =>
        db.GrammarVersions
          .Include(g => g.Family)
          .Include(g => g.Segments).ThenInclude(s => s.Values)
          // Conditions gate whether a segment may match at all, so a parse without them silently
          // accepts contradictory numbers. Loaded on the same path as Values for that reason.
          .Include(g => g.Segments).ThenInclude(s => s.Conditions)
          .Include(g => g.DerivedSegments).ThenInclude(d => d.Rules)
          .Where(g => familyId == null || g.FamilyId == familyId)
          .OrderBy(g => g.FamilyId).ThenByDescending(g => g.IsCurrent).ThenByDescending(g => g.EffectiveFrom)
          .ToListAsync();

    /// Families that can be built from, i.e. those with a grammar, each with its manufacturer.
    ///
    /// Separate from GetVersionsAsync on purpose. That one feeds every parse and resolve call, so it
    /// should not carry an extra join it does not need; and it includes Family but not
    /// Family.Manufacturer, which is what made the builder's manufacturer picker render empty.
    public async Task<List<ModelFamily>> GetBuildableFamiliesAsync()
    {
        var ids = await db.GrammarVersions.Select(g => g.FamilyId).Distinct().ToListAsync();

        return await db.ModelFamilies
            .Include(f => f.Manufacturer)
            .Where(f => ids.Contains(f.Id))
            .OrderBy(f => f.Manufacturer!.Name).ThenBy(f => f.FamilyName)
            .ToListAsync();
    }

    /// Parses against every known grammar and returns the results best-fit first, so a caller
    /// can show "this looks like a 1900/P Rev G number" without being told the family up front.
    public async Task<List<ParseResult>> ParseAllAsync(string modelNumber)
    {
        var versions = await GetVersionsAsync();
        return versions
            .Select(v => Parse(modelNumber, v))
            .Where(r => r.IsViable)          // drop grammars that don't recognise the prefix
            // COMPLETENESS FIRST, then score. A grammar that reads the whole number and satisfies its
            // own required segments is a better fit than one that identifies more segments but leaves
            // the number unexplained -- and the second thing is easy to do by accident.
            //
            // Score alone was not enough once the Crosby J-series arrived. Its grammar has four
            // single-character numeric segments (inlet size, outlet size, inlet rating, temperature
            // range), so it finds spurious partial readings in almost any digit string: `1850` reads
            // as inlet 1, outlet 8, rating 5 with a stray `0`, which is three matched segments and
            // scores 25 -- beating the Farris 1850's correct and COMPLETE two-segment reading at 20.
            // The same accident claimed `1511K` and `1811MB`. Ranking completeness first settles all
            // of them without weakening the anchor, which would have cost real abbreviated numbers.
            .OrderByDescending(r => r.IsComplete)
            .ThenByDescending(r => r.Score)
            .ToList();
    }

    public async Task<ParseResult?> ParseBestAsync(string modelNumber)
        => (await ParseAllAsync(modelNumber)).FirstOrDefault();

    /// Parses `modelNumber` against one grammar version.
    public static ParseResult Parse(string modelNumber, GrammarVersion version)
    {
        var input = (modelNumber ?? "").Trim();
        var segments = version.Segments.OrderBy(s => s.Position).ToList();

        // The input is NOT pre-split. Splitting on '-' before matching is a lossy decision made
        // with no knowledge of the codes, and it silently destroys any code that contains the
        // separator: `1-1/2` arrived as `1` then `1/2`, so the inlet size could never match and
        // quietly degraded to `1` -- a different orifice area, no error raised.
        //
        // Instead the raw string is walked, offering each position to the codes first and treating a
        // separator as structure only when nothing claims it. Token boundaries still matter, but as
        // a CONSTRAINT rather than as pre-cut pieces -- see MatchFrom.
        var walk = new Walk(input, segments, GateSlots(segments));
        var attempt = MatchFrom(walk, 0, 0, atBoundary: true, gates: walk.NoGates);

        var matched = attempt.Matches.ToDictionary(m => m.Position, m => m.Code);
        var leftovers = CoalesceLeftovers(input, attempt.LeftoverAt);

        var parsed = segments.Select(s => new ParsedSegment(
            s.Position, s.SegmentKey, s.Label,
            matched.TryGetValue(s.Position, out var c) ? c : null,
            s.IsRequired,
            // A pattern match has no segment_values row to take a description from, so the segment's
            // own pattern_description stands in. Without this the UI would show a bare value with no
            // indication of what it means -- and "103" with no label is worse than useless on a
            // page a technician reads to identify a part.
            matched.TryGetValue(s.Position, out var c2)
                ? s.Values.FirstOrDefault(v => v.Code == c2)?.Description ?? s.PatternDescription
                : null)).ToList();

        var complete = leftovers.Count == 0
                    && parsed.Where(p => p.IsRequired).All(p => p.Code is not null);

        // Anchor: the EARLIEST segment that matched must have matched at the very start of the
        // input, and every segment ahead of it must be one the grammar permits to be absent.
        //
        // The first clause is what stops a foreign number picking up a spurious partial match --
        // `1705R` otherwise matches `17` as a 1900 interchangeability code and gets presented as a
        // plausible best fit. The second clause is what allows a legitimately abbreviated number to
        // still anchor: the 1511 and 1811 codes begin with the inlet size (`1.5-1511H-...`), but
        // both the manual and everyday usage drop it and write `1511H`. Since inlet size is marked
        // optional in those grammars, skipping it is allowed and the valve type anchors instead.
        // Requiring only optional segments to be skippable is what keeps this from re-opening the
        // `1705R` hole: there, every segment before the spurious match is required and unmatched.
        //
        // Computed before derived segments are appended, so only real segments are considered.
        var firstMatched = parsed.FirstOrDefault(p => p.Code is not null);
        var anchored = firstMatched is not null
                    && input.StartsWith(firstMatched.Code!, StringComparison.OrdinalIgnoreCase)
                    && parsed.Where(p => p.Position < firstMatched.Position).All(p => !p.IsRequired);

        parsed.AddRange(Derive(version, parsed));

        return new ParseResult(
            version.Id, version.VersionLabel, version.FamilyId,
            version.Family?.FamilyName ?? $"Family {version.FamilyId}",
            input, parsed, leftovers, complete, anchored);
    }

    /// Derived positions start well past any real segment so ordering stays stable.
    private const int DerivedPositionBase = 900;

    /// Resolves each derived segment by walking its rules in priority order; first match wins.
    ///
    /// A segment that no rule resolves is returned with a null Code rather than a default. That
    /// is the whole point: `1905-00-N` pins neither seat design nor interchangeability, so its
    /// disc type is genuinely unknown and it must not be counted toward any signature.
    private static List<ParsedSegment> Derive(GrammarVersion version, List<ParsedSegment> real)
    {
        if (version.DerivedSegments.Count == 0) return [];

        var codes = real.Where(s => s.Code is not null)
                        .ToDictionary(s => s.SegmentKey, s => s.Code!, StringComparer.OrdinalIgnoreCase);

        var derived = new List<ParsedSegment>();
        var pos = DerivedPositionBase;

        foreach (var d in version.DerivedSegments.OrderBy(d => d.SegmentKey, StringComparer.Ordinal))
        {
            string? resolved = null, from = null;

            foreach (var rule in d.Rules.OrderBy(r => r.Priority))
            {
                if (!codes.TryGetValue(rule.SourceSegmentKey, out var code)) continue;

                var hit = rule.MatchType switch
                {
                    SegmentMatch.EqualTo  => code.Equals(rule.MatchValue, StringComparison.OrdinalIgnoreCase),
                    SegmentMatch.EndsWith => rule.MatchValue is { Length: > 0 } suffix
                                             && code.EndsWith(suffix, StringComparison.OrdinalIgnoreCase),
                    SegmentMatch.Present  => true,
                    _                     => false,
                };
                if (!hit) continue;

                resolved = rule.ResultCode;
                from = $"{rule.SourceSegmentKey}={code}";
                break;
            }

            derived.Add(new ParsedSegment(
                pos++, d.SegmentKey, d.Label, resolved, IsRequired: false,
                resolved is null ? null : $"derived from {from}", IsDerived: true));
        }

        return derived;
    }

    /// Assembles the canonical written form of a model number from a code per segment.
    ///
    /// Segments with no chosen code are simply skipped, and a skipped segment never leaves a stray
    /// dash behind: the dash belongs to the token, so if the segment that opens a token is absent,
    /// the next present segment opens it instead. That is what keeps a partially-filled template
    /// producing something that still parses -- `1706R` rather than `1706R-----`.
    public static string Assemble(GrammarVersion version, IReadOnlyDictionary<string, string> codes)
    {
        var sb = new System.Text.StringBuilder();
        var pendingDash = false;

        foreach (var seg in version.Segments.OrderBy(s => s.Position))
        {
            if (seg.DashBefore) pendingDash = true;
            if (!codes.TryGetValue(seg.SegmentKey, out var code) || string.IsNullOrWhiteSpace(code))
                continue;

            if (pendingDash && sb.Length > 0) sb.Append('-');
            pendingDash = false;
            sb.Append(code);
        }

        return sb.ToString();
    }

    /// One candidate reading of the input: which code each segment took, and which characters no
    /// segment could explain. Immutable, so a failed branch costs nothing to abandon.
    private sealed record Attempt(
        List<(int Position, string Code)> Matches,
        List<int> LeftoverAt)
    {
        /// The whole input accounted for: the base case every successful branch bottoms out in.
        public static readonly Attempt Exhausted = new([], []);

        public Attempt With(int position, string code) =>
            new([(position, code), .. Matches], LeftoverAt);

        public Attempt WithLeftover(int at) =>
            new(Matches, [at, .. LeftoverAt]);
    }

    /// Characters that punctuate a model number rather than belong to a code.
    ///
    /// A separator is only ever treated as structure once every candidate code has been offered the
    /// position and declined it, which is what lets `/S4` stay a single Farris code while `1850/OL`
    /// still reads the `/` as a break.
    private static bool IsSeparator(char c) => c is '-' or '/' or ' ' or '_';

    /// Best reading of `input` from character `pos` onward, using segments from `segIdx` onward.
    ///
    /// The single most important line here is the dash_before check. `dash_before` records that a
    /// segment OPENS a dash-delimited token, and until now the parser stored that fact and only ever
    /// used it to write numbers back out -- never to read one. Consulting it while matching is what
    /// fixes the class of bug where an optional leading segment eats the first character of the
    /// required segment behind it: the 19000's inlet size offers the code `1`, which is a prefix of
    /// valve type `19`, so `19096LCF-...` decoded as inlet size `1` followed by an inexplicable
    /// `9096LCF`, and every stored 19000 and 1982 number was unreadable. Valve type carries
    /// dash_before, so it can only open a token; position 1 of `19096LCF` is `9`, not a separator,
    /// so an inlet size cannot be sitting in front of it and the whole wrong branch is never taken.
    ///
    /// The converse is deliberately NOT enforced. A segment WITHOUT dash_before is preferred
    /// mid-token but still permitted at a boundary, because real numbers are punctuated loosely and
    /// refusing `1706-RR` for a missing-in-the-template dash would be pedantry rather than accuracy.
    /// Ordering makes the common reading win first; backtracking recovers the loose one.
    ///
    /// Backtracking is what makes the constraint usable rather than merely correct. Rejecting the
    /// wrong branch is no help if the parser has already committed to it, so this explores instead of
    /// marching: a code is tried, and if the remainder cannot be explained the choice is undone. That
    /// is affordable precisely because dash_before prunes so hard -- it also retires the old
    /// whole-token special case, which existed only to stop temperature class `H` eating the `H` of
    /// `1706RR-HP`; high_pressure opens a token and temp_class does not, so the collision is gone.
    ///
    /// The `gates` array carries the codes taken so far for segments that OTHER segments are
    /// conditional on -- see SegmentCondition and Applicable. It has to be threaded through the
    /// recursion rather than read off the finished parse, because a condition decides whether a branch
    /// may be taken at all, and by the time a parse has finished it has already committed.
    private static Attempt MatchFrom(
        Walk w, int pos, int segIdx, bool atBoundary, string?[] gates)
    {
        if (pos >= w.Input.Length) return Attempt.Exhausted;

        // `pos` strictly increases on every recursive call, so this memo needs no cycle guard; it is
        // here purely to keep the search linear in practice rather than exponential.
        //
        // The gate signature belongs in the key. Without it, the sub-result cached while reading a
        // welded number would be handed back while reading a flanged one, and the conditions checked
        // on the way down would be quietly bypassed. It is the empty string for every grammar that
        // has no conditions, i.e. seven of the eight, so the key is unchanged in practice.
        var key = (pos, segIdx, atBoundary, w.Signature(gates));
        if (w.Memo.TryGetValue(key, out var cached)) return cached;

        Attempt? best = null;

        // Two passes, and the order is the whole safety argument for patterns. A pattern matches by
        // SHAPE, so it will happily accept text that is really some other segment's known code --
        // which would turn a wrong reading into a confident-looking complete one, the exact failure
        // mode that made the old `1-1/2` bug dangerous. So every enumerated code for every remaining
        // segment is offered the position first, and a pattern is consulted only once they have all
        // declined it. A pattern can therefore never outrank a known code.
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var look in Candidates(w.Segments, segIdx, atBoundary))
            {
                var seg = w.Segments[look];
                if (seg.DashBefore && !atBoundary) continue;

                // Does this segment even exist on the branch the number is on? The 1700's weld end
                // detail cannot appear on a flanged valve, so offering it there is how an impossible
                // number came to parse as complete and correct.
                if (!Applicable(seg, w, gates)) continue;

                var options = pass == 0 ? EnumeratedMatches(seg, w.Input, pos)
                                        : PatternMatches(seg, w.Input, pos);

                foreach (var code in options)
                {
                    var candidate = MatchFrom(w, pos + code.Length, look + 1,
                                             atBoundary: false, gates: w.WithGate(gates, seg, code))
                                    .With(seg.Position, code);

                    // Returning on the first reading that explains everything is a large saving, but
                    // it takes two conditions, and both were learned the hard way.
                    //
                    // It is only safe for an ENUMERATED code. A pattern matches by shape and will take
                    // text belonging to a later segment: `[A-Z0-9]+` swallowing `X9Q` leaves nothing
                    // over and so would win outright, even though reading `X9` and then the known code
                    // `Q` explains the same characters and names one more segment.
                    //
                    // And it is only safe if the reading still satisfies every REQUIRED segment ahead.
                    // Explaining every character is not the same as reading the number correctly. In
                    // the Crosby J-series, `X` is a legal flange facing AND a legal cap, and the cap is
                    // required: `8T210-JBS-E27X` was being read as facing X with the cap simply
                    // missing, because that reading appeared first and left no leftovers. Both readings
                    // account for the whole string, so leftovers cannot separate them -- but one
                    // describes a valve with no cap.
                    if (pass == 0 && candidate.LeftoverAt.Count == 0
                        && w.RequiredSatisfiedBeyond(look, candidate))
                    {
                        w.Memo[key] = candidate;
                        return candidate;
                    }
                    best = Better(best, candidate);
                }
            }
        }

        if (IsSeparator(w.Input[pos]))
        {
            // Structure, not content -- and it opens a new token.
            best = Better(best, MatchFrom(w, pos + 1, segIdx, atBoundary: true, gates));
        }
        else
        {
            // Nothing in the remaining grammar explains this character. Record it and carry on
            // rather than abandoning the tail, so a number with one stray code still decodes the
            // rest of itself -- the 1900's stray `C` is reported, not fatal.
            //
            // The boundary is spent, though, even though no segment claimed the character. A
            // `dash_before` segment means "there is a separator immediately before me", and after an
            // unexplained character there is not. Passing the boundary through was what let a pattern
            // match a token's SUFFIX: `1042` failed the whole-token rule, so the parser dropped the
            // `1`, arrived at `042` still believing it was at a token start, and matched that --
            // reintroducing the truncated, plausible, wrong value from the other direction.
            // ...unless the character is a code for a segment the branch structure depends on. See
            // Walk.GateCouldMatchHere: dropping a gate is how the search learned to evade every
            // condition, because ignoring which branch a number is on lets it match both.
            //
            // The `best is null` half is a safety valve, not a second rule. Withholding the only
            // remaining move could leave a position with no reading at all, and an unparseable
            // character must never be able to strand the walk.
            if (!w.GateCouldMatchHere(pos, segIdx) || best is null)
                best = Better(best, MatchFrom(w, pos + 1, segIdx, atBoundary: false, gates)
                                    .WithLeftover(pos));
        }

        return w.Memo[key] = best!;
    }

    /// State for one parse: the input, the segments, and the machinery for conditional segments.
    ///
    /// Bundled into a context object because threading gate state turned MatchFrom's parameter list
    /// into something nobody could read at the call site.
    private sealed class Walk(string input, List<SegmentDefinition> segments, Dictionary<string, int> gateSlots)
    {
        public string Input { get; } = input;
        public List<SegmentDefinition> Segments { get; } = segments;
        public Dictionary<(int, int, bool, string), Attempt> Memo { get; } = new();

        /// Segment key -> slot in the gates array, for every segment some other segment is
        /// conditional on. Empty for seven of the eight grammars.
        public Dictionary<string, int> GateSlots { get; } = gateSlots;

        public string?[] NoGates { get; } = new string?[gateSlots.Count];

        /// Memo key component. Constant for a grammar with no conditions, so the common case pays
        /// nothing beyond one reference comparison.
        public string Signature(string?[] gates) =>
            GateSlots.Count == 0 ? "" : string.Join(GateSeparator, gates);

        /// ASCII unit separator: it cannot occur in a model number, so two different gate states can
        /// never run together into the same signature. An unset gate joins as empty, and a code is
        /// never empty, so "unset" stays distinguishable from any real code.
        private const char GateSeparator = (char)0x1F;

        /// `gates` with everything this match tells us about the branch recorded. Copy-on-write: a
        /// branch that is abandoned must not leave its gate behind.
        ///
        /// Two things can fill a slot, and the second is what closes a hole the first leaves open:
        ///
        ///   1. The gate segment matching a code directly — inlet_type = W.
        ///   2. A CONDITIONAL segment matching, which IMPLIES its gate's value. An inlet flange facing
        ///      only exists on a flanged valve, so matching one tells us the branch is F even if the
        ///      number never spelled out an inlet type.
        ///
        /// Without (2), a number carrying segments from BOTH branches and no inlet type at all —
        /// `1715A-2-S-X1-I6-103-XL` — would parse completely clean, because with the gate unset neither
        /// side's condition is enforced. The contradiction is really between the two conditional
        /// segments; the gate is just where it is recorded. Only `equals` implies a definite value.
        public string?[] WithGate(string?[] gates, SegmentDefinition seg, string code)
        {
            string?[]? next = null;

            if (GateSlots.TryGetValue(seg.SegmentKey, out var own))
            {
                next = (string?[])gates.Clone();
                next[own] = code;
            }

            foreach (var c in seg.Conditions)
            {
                if (c.MatchType != SegmentMatch.EqualTo || c.MatchValue is null) continue;
                if (!GateSlots.TryGetValue(c.SourceSegmentKey, out var slot)) continue;
                if ((next ?? gates)[slot] is not null) continue;   // already known; cannot conflict
                next ??= (string?[])gates.Clone();
                next[slot] = c.MatchValue;
            }

            return next ?? gates;
        }

        /// Whether every REQUIRED segment past index `look` is accounted for in this candidate.
        ///
        /// Guards the fast path out of MatchFrom. `Better()` already prefers readings with fewer
        /// leftovers and then more matches, but the early return bypasses it entirely, so without this
        /// a reading that abandons a required segment can win purely by appearing first.
        public bool RequiredSatisfiedBeyond(int look, Attempt candidate)
        {
            for (var i = look + 1; i < Segments.Count; i++)
            {
                if (!Segments[i].IsRequired) continue;
                var position = Segments[i].Position;
                var found = false;
                foreach (var m in candidate.Matches)
                    if (m.Position == position) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        /// Whether some segment that other segments are conditional on could match here, and has not
        /// been passed yet.
        ///
        /// Used to refuse to drop such a character as unexplained. A gate code is not ordinary text:
        /// it selects which branch the number is on, and therefore changes what the REST of the number
        /// is allowed to say. Left droppable, the search simply evaded every condition — reading
        /// `1715WA-2-S-X1-I6-F1-HP` as "no inlet type, one stray W, and a flange facing" scores better
        /// than the honest "welded valve, and a flange facing that cannot be there", because it
        /// explains four more characters. Correct by the letter of the scoring rule, and it presented a
        /// welded valve as flanged.
        public bool GateCouldMatchHere(int pos, int segIdx)
        {
            for (var i = segIdx; i < Segments.Count; i++)
            {
                var seg = Segments[i];
                if (!GateSlots.ContainsKey(seg.SegmentKey)) continue;
                foreach (var v in seg.Values)
                    if (v.Code.Length > 0 && StartsWithCode(Input, pos, v.Code)) return true;
            }
            return false;
        }
    }

    private static Dictionary<string, int> GateSlots(List<SegmentDefinition> segments)
    {
        var keys = segments.SelectMany(s => s.Conditions)
                           .Select(c => c.SourceSegmentKey)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .OrderBy(k => k, StringComparer.Ordinal)
                           .ToList();

        var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < keys.Count; i++) slots[keys[i]] = i;
        return slots;
    }

    /// Whether `seg` exists at all, given what the number has said so far.
    ///
    /// Conditions on one segment are ANDed. The rule that carries the weight is what happens when the
    /// source segment did NOT match: an `equals`/`ends_with` condition is then NOT enforced.
    ///
    /// That is not leniency for its own sake, it is what the data requires. `inlet_type` is optional in
    /// the 1700 grammar -- its seed note says "F = flanged is in the catalog template but omitted in
    /// practice on real numbers" -- and all 52 registered 1700 numbers are stems like `1706RR-HP` with
    /// no inlet type at all. Enforcing against an absent gate would make every one of them unreadable.
    /// A condition is there to catch a CONTRADICTION, and a number that has not said which branch it is
    /// on contradicts nothing yet.
    ///
    /// `present` is the exception, and deliberately so: it asks whether the source segment is there at
    /// all, so an absent source is exactly what it is meant to reject. Currently unused.
    private static bool Applicable(SegmentDefinition seg, Walk w, string?[] gates) =>
        ConditionsHold(seg, key => w.GateSlots.TryGetValue(key, out var slot) ? gates[slot] : null);

    /// Whether a segment applies, given a set of already-chosen codes.
    ///
    /// Exposed for the builder, which is the likeliest place an impossible number would be CREATED
    /// rather than merely read: it renders every segment in the grammar, so without this someone can
    /// pick a flange facing and type a weld end detail and get a number describing a valve that cannot
    /// exist. Shares ConditionsHold with the parser deliberately -- two implementations of this would
    /// eventually disagree, and then the builder would emit numbers the decoder rejects.
    public static bool AppliesGiven(
        GrammarVersion version, SegmentDefinition segment, IReadOnlyDictionary<string, string> chosen)
    {
        if (segment.Conditions.Count == 0) return true;
        var effective = EffectiveCodes(version, chosen);
        return ConditionsHold(segment, key => effective.GetValueOrDefault(key));
    }

    /// Chosen codes, plus the branch values they IMPLY. Mirrors Walk.WithGate: picking an inlet flange
    /// facing means the valve is flanged, whether or not an inlet type was picked as well, so a weld
    /// end detail has to become unavailable on that basis alone.
    private static Dictionary<string, string> EffectiveCodes(
        GrammarVersion version, IReadOnlyDictionary<string, string> chosen)
    {
        var effective = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in chosen)
            if (!string.IsNullOrWhiteSpace(v)) effective[k] = v;

        foreach (var seg in version.Segments)
        {
            if (!effective.ContainsKey(seg.SegmentKey)) continue;
            foreach (var c in seg.Conditions)
            {
                if (c.MatchType != SegmentMatch.EqualTo || c.MatchValue is null) continue;
                if (effective.ContainsKey(c.SourceSegmentKey)) continue;
                effective[c.SourceSegmentKey] = c.MatchValue;
            }
        }

        return effective;
    }

    /// Evaluates a segment's conditions against a lookup of source-segment codes. `codeOf` returns
    /// null for a source that has not matched.
    ///
    /// Conditions on one segment are ANDed. The rule that carries the weight is what happens when the
    /// source segment did NOT match: an `equals`/`ends_with` condition is then NOT enforced.
    ///
    /// That is not leniency for its own sake, it is what the data requires. `inlet_type` is optional in
    /// the 1700 grammar -- its seed note says "F = flanged is in the catalog template but omitted in
    /// practice on real numbers" -- and all 52 registered 1700 numbers are stems like `1706RR-HP` with
    /// no inlet type at all. Enforcing against an absent gate would make every one of them unreadable.
    /// A condition is there to catch a CONTRADICTION, and a number that has not said which branch it is
    /// on contradicts nothing yet.
    ///
    /// `present` is the exception, and deliberately so: it asks whether the source segment is there at
    /// all, so an absent source is exactly what it is meant to reject. Currently unused.
    private static bool ConditionsHold(SegmentDefinition seg, Func<string, string?> codeOf)
    {
        if (seg.Conditions.Count == 0) return true;

        foreach (var c in seg.Conditions)
        {
            var code = codeOf(c.SourceSegmentKey);

            if (c.MatchType == SegmentMatch.Present)
            {
                if (code is null) return false;
                continue;
            }

            if (code is null) continue;

            var ok = c.MatchType switch
            {
                SegmentMatch.EqualTo  => code.Equals(c.MatchValue, StringComparison.OrdinalIgnoreCase),
                SegmentMatch.EndsWith => c.MatchValue is { Length: > 0 } suffix
                                         && code.EndsWith(suffix, StringComparison.OrdinalIgnoreCase),
                _                     => false,
            };
            if (!ok) return false;
        }

        return true;
    }

    /// Segments still available, best-fitting first. At a token boundary the segments that open a
    /// token are offered ahead of those that continue one; otherwise plain position order. LINQ's
    /// sort is stable, so position order survives inside each group.
    private static IEnumerable<int> Candidates(
        List<SegmentDefinition> segments, int segIdx, bool atBoundary)
    {
        var available = Enumerable.Range(segIdx, segments.Count - segIdx);
        return atBoundary ? available.OrderByDescending(i => segments[i].DashBefore) : available;
    }

    /// Prefers the reading that leaves the fewest characters unexplained, then the one that
    /// identifies the most segments.
    private static Attempt Better(Attempt? a, Attempt b)
    {
        if (a is null) return b;
        if (b.LeftoverAt.Count != a.LeftoverAt.Count)
            return b.LeftoverAt.Count < a.LeftoverAt.Count ? b : a;
        return b.Matches.Count > a.Matches.Count ? b : a;
    }

    /// Whether `code` is an acceptable value for a pattern segment.
    ///
    /// Exposed for the builder, which has to tell someone typing a customer weld end detail number
    /// that `12` is not one BEFORE it assembles a number that will not decode again. A model number
    /// that does not parse back is worse than no model number: it looks authoritative and is wrong.
    ///
    /// A segment with no pattern accepts nothing here — use its enumerated codes instead.
    public static bool FitsPattern(SegmentDefinition segment, string? code) =>
        segment.Pattern is { Length: > 0 } p
        && !string.IsNullOrEmpty(code)
        && Anchored(p) is { } rx
        && TryMatch(rx, code);

    private static bool TryMatch(Regex rx, string candidate)
    {
        try { return rx.IsMatch(candidate); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    /// Enumerated codes matching at `pos`, longest first. Longest-first matters: inlet type must
    /// prefer `RFAA` over `RF`. Shorter codes are still tried afterwards, so a greedy match that
    /// strands the tail is no longer fatal.
    private static IEnumerable<string> EnumeratedMatches(SegmentDefinition seg, string input, int pos) =>
        seg.Values.Select(v => v.Code)
                  .Where(c => c.Length > 0 && StartsWithCode(input, pos, c))
                  .OrderByDescending(c => c.Length);

    /// Substrings at `pos` accepted by the segment's pattern, longest first.
    ///
    /// Three guarantees are enforced here rather than left to whoever writes the pattern, because a
    /// pattern is stored data and a mistake in one should not be able to corrupt a decode:
    ///
    ///   * **A match never crosses a separator.** The candidate length is capped at the current
    ///     token, so a careless `.+` cannot swallow the rest of the number.
    ///   * **The pattern accounts for the whole candidate.** It is anchored at both ends, so the
    ///     regex itself cannot quietly match a prefix of what it was handed.
    ///   * **A pattern segment that OPENS a token must account for the whole token.** This one is
    ///     worth spelling out, because a test found the hole. A segment matched by shape has no code
    ///     list to tell it where it ends, so if it also has no leading separator to anchor against,
    ///     the token boundary is the only honest delimiter it has. Without this rule the 1700's
    ///     `[0-9]{3}` weld end detail would read a four-digit customer number `1042` as `104` and
    ///     report a stray `2` -- a plausible, wrong value, which is exactly the failure the narrow
    ///     pattern was chosen to avoid. Requiring the whole token makes it fail instead, and a
    ///     failure is what turns into an RFI.
    ///
    /// For a pattern segment that continues a token, every length down to one character is offered,
    /// longest first, so the parser can still backtrack over how much the pattern took.
    private static IEnumerable<string> PatternMatches(SegmentDefinition seg, string input, int pos)
    {
        if (seg.Pattern is not { Length: > 0 } pattern) yield break;
        if (Anchored(pattern) is null) yield break;

        var limit = pos;
        while (limit < input.Length && !IsSeparator(input[limit])) limit++;

        var longest = limit - pos;
        var shortest = seg.DashBefore ? longest : 1;

        for (var len = longest; len >= shortest; len--)
        {
            // Re-read the cache each time: a pattern that times out is poisoned mid-parse, and this
            // loop is the hot path that would otherwise keep paying the timeout.
            var rx = Anchored(pattern);
            if (rx is null) yield break;

            var candidate = input.Substring(pos, len);
            bool ok;
            try
            {
                ok = rx.IsMatch(candidate);
            }
            catch (RegexMatchTimeoutException)
            {
                // Catastrophic backtracking. One timeout is enough to condemn the pattern: it is a
                // data defect, and paying the timeout again -- once per candidate length, once per
                // memo state -- is how a 100ms budget became five and a half seconds in testing.
                PatternCache[pattern] = null;
                yield break;
            }
            if (ok) yield return candidate;
        }
    }

    /// Compiled, both-ends-anchored form of a stored pattern, cached by pattern text.
    ///
    /// The timeout is not paranoia about the eight patterns we control -- it is that a pattern is a
    /// database value, editable without a deploy, and catastrophic backtracking in one would hang a
    /// Blazor Server circuit rather than fail a request. An invalid pattern yields null and the
    /// segment simply never matches, which is the same as having no codes: the number reads
    /// incompletely and says so, rather than the page throwing.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex?> PatternCache = new();

    private static Regex? Anchored(string pattern) => PatternCache.GetOrAdd(pattern, static p =>
    {
        try
        {
            return new Regex($"^(?:{p})$",
                             RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                             TimeSpan.FromMilliseconds(50));
        }
        catch (ArgumentException) { return null; }
    });

    private static bool StartsWithCode(string input, int pos, string code) =>
        pos + code.Length <= input.Length
        && string.Compare(input, pos, code, 0, code.Length, StringComparison.OrdinalIgnoreCase) == 0;

    /// Turns unexplained character positions back into readable runs, so a caller sees the stray
    /// `WSC` it typed rather than three separate letters.
    private static List<string> CoalesceLeftovers(string input, List<int> positions)
    {
        var runs = new List<string>();
        foreach (var at in positions.Distinct().OrderBy(i => i))
        {
            if (runs.Count > 0 && at > 0 && positions.Contains(at - 1))
                runs[^1] += input[at];
            else
                runs.Add(input[at].ToString());
        }
        return runs;
    }
}
