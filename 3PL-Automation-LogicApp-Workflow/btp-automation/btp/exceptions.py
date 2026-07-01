"""BTP automation exceptions."""


class BTPError(Exception):
    """Base exception for BTP operations."""
    def __init__(self, message: str, status_code: int = None, response: dict = None):
        super().__init__(message)
        self.status_code = status_code
        self.response = response or {}


class BTPNotFoundError(BTPError):
    """Resource not found (404)."""


class BTPAuthError(BTPError):
    """Authentication or authorization failure (401/403)."""


class BTPConflictError(BTPError):
    """Resource already exists (409)."""


class BTPValidationError(BTPError):
    """Bad request / validation failure (400)."""
