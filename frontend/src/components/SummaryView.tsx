import {
  Cell,
  Legend,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
} from 'recharts';
import { CategorizationResult } from '../api';
import EngineBadge from './EngineBadge';

const COLORS = ['#2f6fed', '#16a34a', '#f59e0b', '#ef4444', '#8b5cf6', '#0ea5e9', '#db2777'];

export default function SummaryView({ result }: { result: CategorizationResult }) {
  const data = result.summary.map((s) => ({ name: s.category, value: s.count }));

  return (
    <section className="card summary-card">
      <div className="summary-header">
        <h2>Summary</h2>
        <EngineBadge source={result.source} />
      </div>
      <p className="muted">
        Batch <code>{result.batchId}</code> · {result.totalRecords} tickets · {result.summary.length} categories
        {' · '}
        <strong>{result.duplicateCount}</strong> duplicate{result.duplicateCount === 1 ? '' : 's'} flagged
      </p>

      <div className="summary-body">
        <div className="chart">
          <ResponsiveContainer width="100%" height={300}>
            <PieChart>
              <Pie
                data={data}
                dataKey="value"
                nameKey="name"
                cx="50%"
                cy="50%"
                outerRadius={100}
                label={(e: any) => `${e.value}`}
              >
                {data.map((_, i) => (
                  <Cell key={i} fill={COLORS[i % COLORS.length]} />
                ))}
              </Pie>
              <Tooltip />
              <Legend />
            </PieChart>
          </ResponsiveContainer>
        </div>

        <table className="summary-table">
          <thead>
            <tr>
              <th>Category</th>
              <th>Count</th>
              <th>%</th>
            </tr>
          </thead>
          <tbody>
            {result.summary.map((s) => (
              <tr key={s.category}>
                <td>{s.category}</td>
                <td>{s.count}</td>
                <td>{s.percentage}%</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {result.groups.length > 0 && (
        <div className="issue-groups">
          <h3>Recurring issue groups</h3>
          <p className="muted">Similar tickets clustered together — focus here to fix root causes, not symptoms.</p>
          <table className="summary-table">
            <thead>
              <tr>
                <th>Issue Group</th>
                <th>Category</th>
                <th>Count</th>
                <th>Example</th>
              </tr>
            </thead>
            <tbody>
              {result.groups.map((g, i) => (
                <tr key={i}>
                  <td>{g.signature}</td>
                  <td>{g.category}</td>
                  <td>{g.count}</td>
                  <td>{g.representativeIncident}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
