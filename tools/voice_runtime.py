"""Short-lived CPU inference host. Started by the pet only for new speech."""
import argparse
import ctypes
from ctypes import wintypes
import os
from pathlib import Path
import runpy
import sys
import threading


def watch_parent(pid):
    # A real Windows process handle avoids PID reuse and polling. Also covers app crashes.
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel.WaitForSingleObject.restype = wintypes.DWORD
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.OpenProcess(0x00100000, False, pid)
    if not handle:
        raise RuntimeError("The desktop pet is no longer available.")

    def wait():
        kernel.WaitForSingleObject(handle, 0xFFFFFFFF)
        kernel.CloseHandle(handle)
        os._exit(0)

    threading.Thread(target=wait, daemon=True).start()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", required=True)
    parser.add_argument("--config", required=True)
    parser.add_argument("--host", required=True)
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--parent-pid", required=True, type=int)
    parser.add_argument("--token", required=True)
    args = parser.parse_args()
    watch_parent(args.parent_pid)
    repo = Path(args.repo).resolve()
    config = Path(args.config).resolve()  # A per-request copy, never the user's source YAML.
    os.chdir(repo)
    sys.path[:0] = [str(repo), str(repo / "GPT_SoVITS")]
    os.environ.update(CUDA_VISIBLE_DEVICES="-1", _CUDA_VISIBLE_DEVICES="-1",
                      OMP_NUM_THREADS="2", MKL_NUM_THREADS="2", TOKENIZERS_PARALLELISM="false",
                      USE_LIBUV="0")
    os.environ["PATH"] = str(repo / "runtime") + os.pathsep + os.environ["PATH"]
    import yaml
    settings = yaml.safe_load(config.read_text(encoding="utf-8"))
    for section in settings.values():
        if isinstance(section, dict):
            section.update(device="cpu", is_half=False)
    config.write_text(yaml.safe_dump(settings), encoding="utf-8")
    import torch
    torch.set_num_threads(2)
    torch.backends.mkldnn.enabled = False
    sys.argv = [str(repo / "api_v2.py"), "-a", args.host, "-p", str(args.port), "-c", str(config)]
    namespace = runpy.run_path(str(repo / "api_v2.py"), run_name="yierpet_voice_api")
    app = namespace["APP"]

    @app.get("/yierpet-runtime")
    def identity():
        return {"token": args.token, "pid": os.getpid()}

    import uvicorn
    uvicorn.run(app=app, host=args.host, port=args.port, workers=1, access_log=False)


if __name__ == "__main__":
    main()
