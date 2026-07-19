### Finding 1: Deploy Template Ships Active Weak Secret Defaults
**Status**: Resolved
**Resolution**: The deployment template now leaves SECRET_KEY, GAMEAPI_SHARED_SECRET, and PINDER_DATABASE_URL inert so copying it cannot preserve weak credentials. FastAPI and GameApi startup centrally reject known unsafe placeholder values without logging the supplied secrets or database credentials. The integrated pinder-web commit is 13be4103.
**Verification**: The integration branch passed 8 focused FastAPI configuration-validation tests and 34 GameApi startup fail-fast tests. The worker also passed the version-bump gate and left a clean committed worktree.

