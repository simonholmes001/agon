import HeroSection from "@/components/landing/hero-section";
import ThemeToggle from "@/components/theme-toggle";

export default function Home() {
  return (
    <div className="min-h-[100dvh] bg-background">
      <div className="fixed right-4 top-4 z-50">
        <ThemeToggle />
      </div>
      <HeroSection />
    </div>
  );
}
