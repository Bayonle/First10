import { useQuery } from '@tanstack/react-query';
import { fetchStats } from '../api';

/**
 * The §8.3 KPI ledger — the same numbers the panel deck's evaluation slide needs,
 * live off the ticket ledger. Rendered as a manifest table, one row per metric,
 * with the project paper's target beside each measured value.
 */
export default function StatsPage() {
  const stats = useQuery({ queryKey: ['stats'], queryFn: fetchStats, refetchInterval: 30_000 });
  const s = stats.data;

  if (stats.isLoading) return <p className="text-ink-faint">Loading…</p>;
  if (!s) return <p className="font-semibold text-sev">Failed to load stats.</p>;

  const pct = (v: number) => `${(v * 100).toFixed(0)}%`;
  const rows: [string, string, string, boolean | null][] = [
    // label, measured, paper target, on-target (null = not yet assessable)
    ['Reports processed', String(s.reportsTotal), '≥ 30 by pilot close', s.reportsTotal >= 30],
    ['Verified incidents', String(s.verifiedReports), '—', null],
    ['Multi-reporter incidents', String(s.multiReporterIncidents), '100% coherent timeline', null],
    ['Open queue depth', String(s.openQueueDepth), '—', null],
    [
      'Median time-to-dispatch',
      s.medianTimeToDispatchMinutes === null ? 'no dispatches yet' : `${s.medianTimeToDispatchMinutes.toFixed(1)} min`,
      '≤ 5 min (baseline ~25 min)',
      s.medianTimeToDispatchMinutes === null ? null : s.medianTimeToDispatchMinutes <= 5,
    ],
    [
      'Median instruction latency',
      s.medianInstructionLatencySeconds === null ? 'none sent' : `${s.medianInstructionLatencySeconds.toFixed(0)} s`,
      '≤ 30 s',
      s.medianInstructionLatencySeconds === null ? null : s.medianInstructionLatencySeconds <= 30,
    ],
    ['Instruction coverage', pct(s.instructionCoverageRate), '≥ 90% of verified', s.instructionCoverageRate >= 0.9],
    ['Loop-closure rate', pct(s.loopClosureRate), '≥ 80% of verified', s.loopClosureRate >= 0.8],
    [
      'False-positive rate',
      s.falsePositiveRate === null ? `no outcomes marked (${s.outcomesMarked})` : pct(s.falsePositiveRate),
      '< 5%',
      s.falsePositiveRate === null ? null : s.falsePositiveRate < 0.05,
    ],
  ];

  return (
    <div className="max-w-3xl">
      <h2 className="microlabel mb-1 border-b border-hairline-strong pb-1">
        Pilot evaluation ledger
      </h2>
      <p className="mb-4 text-[0.8rem] text-ink-faint">
        Live from the incident ledger. Baseline time-to-dispatch (~25 min via 122 calls) is
        measured with the FRSC duty officer before soft launch and compared externally.
      </p>
      <table className="w-full border-collapse text-sm">
        <thead>
          <tr className="font-display text-left text-[0.68rem] uppercase tracking-widest text-ink-soft">
            <th className="border-b-2 border-ink py-2 pr-4 font-bold">Metric</th>
            <th className="border-b-2 border-ink py-2 pr-4 font-bold">Measured</th>
            <th className="border-b-2 border-ink py-2 pr-4 font-bold">Paper target</th>
            <th className="border-b-2 border-ink py-2 font-bold">Status</th>
          </tr>
        </thead>
        <tbody>
          {rows.map(([label, measured, target, ok]) => (
            <tr key={label} className="border-b border-hairline">
              <td className="py-2.5 pr-4 font-medium">{label}</td>
              <td className="py-2.5 pr-4 font-mono">{measured}</td>
              <td className="py-2.5 pr-4 text-ink-soft">{target}</td>
              <td className="py-2.5">
                {ok === null ? (
                  <span className="stamp text-ink-faint">pending</span>
                ) : ok ? (
                  <span className="stamp text-ok">on target</span>
                ) : (
                  <span className="stamp text-sev">off target</span>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
