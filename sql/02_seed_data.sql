-- ─────────────────────────────────────────────
--  02_seed_data.sql
--  Demo rows — used to verify replication is working
-- ─────────────────────────────────────────────

INSERT INTO datasets (title, doi, description) VALUES
    ('5G Network Latency Measurements — Athens 2024', '10.1234/slices.ds.001', 'Latency benchmarks collected across 3 SLICES testbeds'),
    ('Wi-Fi 6E Throughput Dataset',                   '10.1234/slices.ds.002', 'Throughput measurements at 6 GHz band'),
    ('Edge Computing Load Profiles',                  '10.1234/slices.ds.003', 'CPU and memory profiles from 12 edge nodes');

INSERT INTO publications (title, authors, journal, year, doi) VALUES
    ('SLICES: A Scientific Large-scale Infrastructure', 'Nikaein N. et al.', 'IEEE Communications', 2023, '10.1109/slice.2023.001'),
    ('Reproducible 5G Experiments on Testbeds',         'Korakis T. et al.', 'ACM SIGCOMM',         2024, '10.1145/sigcomm.2024.002');

INSERT INTO software_tools (name, version, language, repository_url) VALUES
    ('MRS Backend',   '1.2.0', 'Python',     'https://github.com/slices-ri/mrs-backend'),
    ('slices-dm CLI', '0.9.1', 'Go',         'https://github.com/slices-ri/slices-dm'),
    ('MRS Portal',    '2.0.0', 'TypeScript', 'https://github.com/slices-ri/mrs-portal');