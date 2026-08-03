import { Gem } from 'lucide-react';

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <main className="flex min-h-dvh items-center justify-center bg-muted/40 px-4 py-10">
      <div className="w-full max-w-sm space-y-6">
        <div className="flex flex-col items-center gap-2">
          <span className="flex size-11 items-center justify-center rounded-xl bg-primary text-primary-foreground">
            <Gem className="size-5" />
          </span>
          <div className="text-center">
            <h1 className="text-xl font-semibold tracking-tight">LeadMine AI</h1>
            <p className="text-sm text-muted-foreground">Backend account access</p>
          </div>
        </div>
        {children}
      </div>
    </main>
  );
}
