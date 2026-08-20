# M1 independent CI verification

This small documentation commit intentionally triggers the permanent GitHub Actions matrix after the one-time M1 bootstrap published the tested source tree. The permanent matrix independently rebuilds the C++ rules core on GCC, Clang sanitizers and MSVC, compiles the managed M1 model and Unity-facing overlay, runs the M1 interaction smoke tests, and applies the overlay to the locked YGOPro2 source.
