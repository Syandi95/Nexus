"""Option B inventory/selection/result checks; stdlib-only development tooling.

This module never launches or terminates a process and never converts a failed
xUnit result into a passed result. The certification wrapper owns execution.
"""
import json
import re
from collections import Counter
from pathlib import Path
import xml.etree.ElementTree as ET

CONTRACT = 'NEXUS-GATE-H-OPTION-B-V2'
P1 = 'Nexus.Integration.Tests.GateHP1NativeSelectionLiveTests.CandidateArrays_ProductionAdapterAndDecoder_ReadOnlyColdWarmChat'
TRAIT = 'GateHP1Reliability'
FILTERS = {'mandatory': 'Category!=GateHP1Reliability',
           'diagnostic': 'Category=GateHP1Reliability&FullyQualifiedName=' + P1}
REJECTIONS = {'OutputLimit', 'MissingNativeToolCall', 'MalformedNativeResponse',
              'InvalidChoices', 'InvalidAssistantRole', 'InvalidToolCalls',
              'InvalidToolCallId', 'DuplicateToolCallId', 'InvalidCallType',
              'InvalidToolName', 'ArgumentLimit', 'ToolCallLimit',
              'InvalidToolFinishReason', 'UnexpectedCallMetadata',
              'DuplicateNativeProperty', 'InvalidCallShape', 'ResponseLimit',
              'UnexpectedToolName'}

def partition(manifest, inventory):
    if (manifest['contract'] != CONTRACT or manifest['diagnostic_test'] != P1 or
        manifest['mandatory_filter'] != FILTERS['mandatory'] or
        manifest['diagnostic_filter'] != FILTERS['diagnostic']):
        raise ValueError('Unreviewed acceptance/filter change')
    cases = inventory['cases']
    keys = [(Path(c['source']).name.lower(), c['id']) for c in cases]
    if len(set(keys)) != len(keys) or len({c['id'] for c in cases}) != len(cases):
        raise ValueError('Duplicate discovered test identity')
    expected = {Path(p).stem + '.dll' for p in manifest['test_projects']}
    if {Path(c['source']).name for c in cases} != expected:
        raise ValueError('Missing or unknown solution test assembly')
    diagnostic = [c for c in cases if TRAIT in c['traits'].get('Category', [])]
    if len(diagnostic) != 1 or diagnostic[0]['fullName'] != P1:
        raise ValueError('Only the exact single P1 probe may be diagnostic')
    mandatory = [c for c in cases if c not in diagnostic]
    names = {c['fullName'] for c in mandatory}
    if not set(manifest['required_mandatory_live_tests']) <= names:
        raise ValueError('Missing mandatory Gate A-H fixture')
    if inventory.get('aborted') or inventory.get('errors'):
        raise ValueError('Incomplete/failed discovery')
    return {'mandatory': mandatory, 'diagnostic': diagnostic}

def selected_command(command, suite, manifest):
    if suite not in FILTERS or '--filter' in command or any(a.startswith('--filter=') for a in command):
        raise ValueError('Unknown suite or pre-filtered command')
    if manifest[suite + '_filter'] != FILTERS[suite]:
        raise ValueError('Unreviewed filter')
    if command[1:3] != ['test', 'src/Nexus.sln']:
        raise ValueError('Certification must cover the declared solution')
    return [*command, '--filter', FILTERS[suite]]

def trx_results(directory):
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    rows = []
    for file in sorted(Path(directory).glob('*.trx')):
        tree = ET.parse(file)
        summary = tree.find('t:ResultSummary', ns)
        if summary is None or summary.get('outcome') == 'Aborted':
            raise ValueError('Missing/aborted TRX summary')
        for result in tree.findall('t:Results/t:UnitTestResult', ns):
            rows.append({'id': result.attrib['testId'], 'displayName': result.attrib['testName'],
                         'source': Path(file).name, 'outcome': result.attrib['outcome'],
                         'trace': '\n'.join(result.itertext())})
    return rows

def summarize_suite(manifest, inventory, suite, rows, evidence_directory=None):
    expected = partition(manifest, inventory)[suite]
    expected_ids = {c['id'] for c in expected}
    actual = Counter(r['id'] for r in rows)
    if any(n != 1 for n in actual.values()) or set(actual) - expected_ids:
        raise ValueError('Duplicate, unknown or wrong-suite executed result')
    missing = sorted(expected_ids - set(actual))
    counts = Counter(r['outcome'] for r in rows)
    if set(counts) - {'Passed', 'Failed', 'NotExecuted'}:
        raise ValueError('Unsupported/aborted result outcome')
    proofs = []
    if evidence_directory is not None:
        proofs = [json.loads(p.read_text(encoding='utf-8')) for p in Path(evidence_directory).glob('array-*/array-proof.json')]
    classification = None
    if suite == 'diagnostic':
        complete_proof = (len(proofs) == 1 and [r.get('mode') for r in proofs[0].get('records', [])] == ['cold', 'warm', 'chat'] and
                          proofs[0].get('failure') is None and all(r.get('adequate') is True and
                          all(r.get(k) == 0 for k in ('sourceWrites', 'toolDispatches', 'approvalRequests')) for r in proofs[0]['records']))
        classification = 'PASS' if counts['Passed'] == 1 and not missing and complete_proof else 'MANDATORY BLOCKER — missing/skip/host defect or insufficient failure evidence'
        if counts['Failed'] == 1 and len(proofs) == 1:
            f = proofs[0].get('failure') or {}
            reason = f.get('reasonCode')
            rejected = (f.get('stage') == 'NativeGeneration' and reason in REJECTIONS and
                        f.get('errorType') == 'Nexus.App.Services.Agent.ToolProtocolException' and
                        f.get('nativeSuccess') is False and
                        all(f.get(k) == 0 for k in ('sourceWrites', 'toolDispatches', 'approvalRequests')) and
                        re.search(r'ToolProtocolException\s*:\s*' + re.escape(reason) + r'\b', rows[0]['trace']) is not None)
            if rejected:
                classification = 'RELIABILITY FAILURE — ' + reason
    successes = [r for p in proofs for r in p.get('records', [])]
    failures = [p['failure'] for p in proofs if p.get('failure')]
    return {'suite': suite, 'total_discovered': len(inventory['cases']),
            'discovered': len(expected), 'executed': len(rows), 'pass': counts['Passed'],
            'fail': counts['Failed'], 'skip': counts['NotExecuted'], 'missing': missing,
            'diagnostic_failure_classification': classification,
            'diagnostic_evidence': proofs,
            'native_generation_successes': len(successes),
            'OutputLimit': sum(f.get('reasonCode') == 'OutputLimit' for f in failures),
            'invalid_tool_response': sum(f.get('reasonCode') in REJECTIONS - {'OutputLimit'} for f in failures),
            'conditions': [r.get('mode') for r in successes] + [f.get('mode') for f in failures],
            'prompt_completion_tokens': [{'mode': r.get('mode'), 'usage': r.get('usage')} for r in successes + failures],
            'actual_failure_traces': [r['trace'] for r in rows if r['outcome'] == 'Failed'],
            'mandatory_blocker': bool(missing or counts['NotExecuted'] or
                                      (suite == 'diagnostic' and (classification or '').startswith('MANDATORY BLOCKER')) or
                                      (counts['Failed'] and (suite == 'mandatory' or
                                       not (classification or '').startswith('RELIABILITY FAILURE'))))}

def combined_summary(mandatory, diagnostic, invocations=None):
    invocation_blocker = False
    if invocations is not None:
        a, b = invocations
        invocation_blocker = (a['candidate_id'] != b['candidate_id'] or a['candidate_sha256'] != b['candidate_sha256'] or
                              a['exit_code'] != 0 or (b['exit_code'] != 0 and not (diagnostic['fail'] == 1 and not diagnostic['mandatory_blocker'])) or
                              any(r.get('first_failure') or r.get('remaining_owned_processes') or
                                  not all(r.get(k) is True for k in ('source_hashes_match', 'source_inventory_match', 'binary_hashes_match', 'reader_completed')) for r in invocations))
    blocked = mandatory['mandatory_blocker'] or diagnostic['mandatory_blocker'] or invocation_blocker
    return {'total_discovered': mandatory['total_discovered'], 'mandatory': mandatory,
            'diagnostic': diagnostic,
            'mandatory_certification_pass': not blocked,
            'complete_reviewable_result': not blocked,
            'invocation_blocker': invocation_blocker,
            'diagnostic_test_pass': diagnostic['pass'] == 1,
            'gate_i': 'HOLD'}

if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--inventory', required=True)
    parser.add_argument('--mandatory-results', required=True)
    parser.add_argument('--diagnostic-results', required=True)
    parser.add_argument('--diagnostic-evidence', required=True)
    parser.add_argument('--mandatory-invocation', required=True)
    parser.add_argument('--diagnostic-invocation', required=True)
    args = parser.parse_args()
    manifest = json.loads(Path(args.manifest).read_text(encoding='utf-8'))
    inventory = json.loads(Path(args.inventory).read_text(encoding='utf-8'))
    summary=combined_summary(
        summarize_suite(manifest, inventory, 'mandatory', trx_results(args.mandatory_results)),
        summarize_suite(manifest, inventory, 'diagnostic', trx_results(args.diagnostic_results), args.diagnostic_evidence),
        [json.loads(Path(p).read_text(encoding='utf-8')) for p in [args.mandatory_invocation,args.diagnostic_invocation]])
    print(json.dumps(summary,indent=2))
    raise SystemExit(0 if summary['complete_reviewable_result'] else 1)
