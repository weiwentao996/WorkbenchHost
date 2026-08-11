package workspace

import (
	"database/sql"
	"errors"
)

// Store owns the database connection used by the workspace services.
type Store struct {
	db *sql.DB
}

// NewStore validates a connection before exposing it to callers.
func NewStore(db *sql.DB) (*Store, error) {
	if db == nil {
		return nil, errors.New("database connection is nil")
	}
	if err := db.Ping(); err != nil {
		return nil, err
	}
	return &Store{db: db}, nil
}

// Close releases the underlying connection.
func (s *Store) Close() error {
	if s == nil || s.db == nil {
		return nil
	}
	return s.db.Close()
}
