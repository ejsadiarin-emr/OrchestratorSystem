import * as React from "react"
import { Dialog as ModalPrimitive } from "@base-ui/react/dialog"

import { cn } from "@/lib/utils"
import { Button } from "@/components/ui/button"
import { XIcon } from "lucide-react"

function Modal({ ...props }: ModalPrimitive.Root.Props) {
  return <ModalPrimitive.Root data-slot="modal" {...props} />
}

function ModalTrigger({ ...props }: ModalPrimitive.Trigger.Props) {
  return <ModalPrimitive.Trigger data-slot="modal-trigger" {...props} />
}

function ModalClose({ ...props }: ModalPrimitive.Close.Props) {
  return <ModalPrimitive.Close data-slot="modal-close" {...props} />
}

function ModalPortal({ ...props }: ModalPrimitive.Portal.Props) {
  return <ModalPrimitive.Portal data-slot="modal-portal" {...props} />
}

function ModalOverlay({ className, ...props }: ModalPrimitive.Backdrop.Props) {
  return (
    <ModalPrimitive.Backdrop
      data-slot="modal-overlay"
      className={cn(
        "fixed inset-0 z-50 bg-black/45 transition-opacity duration-150 data-ending-style:opacity-0 data-starting-style:opacity-0",
        className,
      )}
      {...props}
    />
  )
}

function ModalContent({
  className,
  children,
  showCloseButton = true,
  ...props
}: ModalPrimitive.Popup.Props & {
  showCloseButton?: boolean
}) {
  return (
    <ModalPortal>
      <ModalOverlay />
      <ModalPrimitive.Popup
        data-slot="modal-content"
        className={cn(
          "fixed top-1/2 left-1/2 z-50 flex w-[min(92vw,48rem)] max-h-[90vh] -translate-x-1/2 -translate-y-1/2 flex-col gap-4 overflow-hidden rounded-xl border border-[var(--surface-border)] bg-[var(--surface)] text-sm text-[var(--text-strong)] shadow-xl transition duration-200 ease-out data-ending-style:opacity-0 data-ending-style:scale-95 data-starting-style:opacity-0 data-starting-style:scale-95",
          className,
        )}
        {...props}
      >
        {children}
        {showCloseButton && (
          <ModalPrimitive.Close
            data-slot="modal-close"
            render={<Button variant="ghost" className="absolute top-3 right-3" size="icon-sm" />}
          >
            <XIcon />
            <span className="sr-only">Close</span>
          </ModalPrimitive.Close>
        )}
      </ModalPrimitive.Popup>
    </ModalPortal>
  )
}

function ModalHeader({ className, ...props }: React.ComponentProps<"div">) {
  return <div data-slot="modal-header" className={cn("flex flex-col gap-0.5 p-4", className)} {...props} />
}

function ModalFooter({ className, ...props }: React.ComponentProps<"div">) {
  return <div data-slot="modal-footer" className={cn("mt-auto flex flex-col gap-2 p-4", className)} {...props} />
}

function ModalTitle({ className, ...props }: ModalPrimitive.Title.Props) {
  return (
    <ModalPrimitive.Title
      data-slot="modal-title"
      className={cn("font-heading text-base font-medium text-foreground", className)}
      {...props}
    />
  )
}

function ModalDescription({ className, ...props }: ModalPrimitive.Description.Props) {
  return <ModalPrimitive.Description data-slot="modal-description" className={cn("text-sm text-muted-foreground", className)} {...props} />
}

export {
  Modal,
  ModalTrigger,
  ModalClose,
  ModalContent,
  ModalHeader,
  ModalFooter,
  ModalTitle,
  ModalDescription,
}
