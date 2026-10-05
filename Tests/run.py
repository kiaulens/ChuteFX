"""Generate the ChuteFX offline tests and run them in Unity batch mode.

Usage:
    python run.py [island] [ground] [normal] [sample]     (default: all)
Environment:
    CHUTEFX_UNITY_PROJECT  a Unity 2019.4 project (an empty one will do)
    UNITY_EXE              Unity.exe of version 2019.4.18f1 (KSP's version)
The ground test also reads CHUTE_OUT, CHUTE_TAG, CHUTE_WATER, CHUTE_LAYER and CHUTE_SMOOTH (see templates/ground.cs).
Prints the result lines of each test; the full Unity logs are written to <project>/ChuteFXTestOutput/.
"""
import os
import re
import subprocess
import sys

import generate

METHODS = {
    'island': ('ChuteFXTest.IslandTest.Run', 'CHUTETEST'),
    'ground': ('ChuteFXGround.GroundTest.Run', 'CHUTEGROUND'),
    'normal': ('ChuteFXNormal.NormalTest.Run', 'CHUTENORMAL'),
    'sample': ('ChuteFXSample.SampleTest.Run', 'CHUTESAMPLE'),
}


def main():
    project = os.environ.get('CHUTEFX_UNITY_PROJECT')
    unity = os.environ.get('UNITY_EXE')
    if not project or not unity:
        sys.exit('set CHUTEFX_UNITY_PROJECT and UNITY_EXE (see the docstring)')
    names = sys.argv[1:] or list(METHODS)
    sys.argv = [sys.argv[0], project]
    generate.main()
    out = os.path.join(project, 'ChuteFXTestOutput')
    os.makedirs(out, exist_ok=True)
    for name in names:
        method, prefix = METHODS[name]
        log = os.path.join(out, name + '.log')
        # not -nographics: the ground test renders images
        subprocess.call([unity, '-batchmode', '-quit', '-projectPath', project, '-executeMethod', method, '-logFile', log])
        text = open(log, encoding='utf-8', errors='replace').read() if os.path.exists(log) else ''
        lines = [l for l in text.splitlines() if l.startswith(prefix)]
        errors = re.findall(r'^.*error CS\d+.*$', text, re.M)
        print('== ' + name + ' ==')
        for l in errors[:20] + lines:
            print(l)
        if not lines and not errors:
            print('no result lines - see ' + log)


if __name__ == '__main__':
    main()
