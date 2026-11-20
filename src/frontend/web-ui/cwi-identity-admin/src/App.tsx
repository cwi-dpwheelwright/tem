import { Navigate, Route, Routes } from 'react-router-dom';
import { AuthGuard } from './components/AuthGuard';
import { Layout } from './components/Layout';
import Login from './pages/Login';
import Tenants from './pages/Tenants';
import Users from './pages/Users';

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<Login />} />
      <Route
        path="/"
        element={
          <AuthGuard>
            <Layout>
              <Navigate to="/tenants" replace />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/tenants"
        element={
          <AuthGuard>
            <Layout>
              <Tenants />
            </Layout>
          </AuthGuard>
        }
      />
      <Route
        path="/users"
        element={
          <AuthGuard>
            <Layout>
              <Users />
            </Layout>
          </AuthGuard>
        }
      />
      <Route path="*" element={<Navigate to="/tenants" replace />} />
    </Routes>
  );
}
